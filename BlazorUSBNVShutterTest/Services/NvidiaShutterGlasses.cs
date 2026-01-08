using SpawnDev;
using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;
using System.Buffers.Binary;

namespace BlazorUSBNVShutterTest.Services
{
    public class NvidiaShutterGlasses : IBackgroundService
    {
        const long NVSTUSB_CLOCK = 48000000L;
        const long NVSTUSB_T0_CLOCK = NVSTUSB_CLOCK / 12L;
        const long NVSTUSB_T2_CLOCK = NVSTUSB_CLOCK / 4L;
        static uint NVSTUSB_T2_COUNT(double us) => (uint)(-us * (NVSTUSB_T2_CLOCK / 1000000L) + 1);
        const byte NVSTUSB_CMD_SET_EYE = 0xAA;
        public bool InvertEyes { get; set; }
        int EndpointNumber = 2;

        BlazorJSRuntime JS;
        public USBDevice? Device;
        USB? USB = null;
        public event Action OnConnected = default!;
        public event Action OnDisconnected = default!;
        HttpClient HttpClient;
        public bool Supported => USB != null;
        List<(int vendorId, int productId)> SupportedDevices = new List<(int vendorId, int productId)>
        {
            (0x0955, 0x0007) // NVIDIA 3D Vision USB IR Emitter
        };
        public NvidiaShutterGlasses(BlazorJSRuntime js, HttpClient httpClient)
        {
            JS = js;
            HttpClient = httpClient;
            if (JS.IsBrowser == false) return;
            using var navigator = JS.Get<Navigator>("navigator");
            USB = navigator.Usb;
            if (USB != null)
            {
                USB.OnConnect += USB_OnConnect;
                USB.OnDisconnect += USB_OnDisconnect;
            }
        }
        void USB_OnConnect(USBConnectionEvent e)
        {
            JS.Log("USB OnConnect", e);
            if (Device == null)
            {
                // try to reconnect to paired device
                _ = ReconnectToPaired();
            }
        }
        void USB_OnDisconnect(USBConnectionEvent e)
        {
            JS.Log("USB OnDisconnect", e);
            if (Device != null)
            {
                using var disconnectedDevice = e.Device;
                var sameDevice = Device.JSEquals(disconnectedDevice);
                if (sameDevice)
                {
                    Device.Dispose();
                    Device = null;
                    OnDisconnected?.Invoke();
                }
            }
        }
        public async Task ReconnectToPaired()
        {
            if (Device != null) return;
            var devices = await FindSupportedDevices();
            if (devices.Count > 0)
            {
                await SetDevice(devices[0]);
            }
        }
        public async Task<List<USBDevice>> FindSupportedDevices()
        {
            var ret = new List<USBDevice>();
            if (USB == null) return ret;
            var devices = await USB.GetDevices();
            JS.Log("_devices", devices);
            foreach (var device in devices)
            {
                foreach (var sd in SupportedDevices)
                {
                    if (device.VendorId == sd.vendorId && device.ProductId == sd.productId)
                    {
                        ret.Add(device);
                        break;
                    }
                }
            }
            return ret;
        }
        public async Task Disconnect()
        {
            if (USB == null) return;
            if (Device == null) return;
            try
            {
                if (Device.Opened)
                {
                    await Device.Close();
                }
            }
            catch (JSException ex)
            {
                JS.Log("Device close failed", ex.ToString());
            }
            Device.Dispose();
            Device = null;
            OnDisconnected?.Invoke();
        }
        /// <summary>
        /// https://github.com/bobsomers/3dvgl/blob/fb9deccb78f3ece4884da0e4ed3da316ec324a2d/lib/usb_libusb.c#L152
        /// </summary>
        /// <param name="reconnectOnly"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public async Task Connect(bool reconnectOnly = false)
        {
            if (USB == null) return;
            if (Device != null) return;
            // first try to connect to any already authorized devices
            await ReconnectToPaired();
            // if Device is now set, or we are reconnectOnly, return
            if (Device != null || reconnectOnly) return;
            var device = await USB.RequestDevice(new USBDeviceRequestOptions
            {
                Filters = SupportedDevices.Select(v => new USBDeviceFilter
                {
                    VendorId = v.vendorId,
                    ProductId = v.productId
                }).ToArray()
            });
            if (device == null) throw new Exception("Device not found.");
            await SetDevice(device);
        }
        async Task SetDevice(USBDevice device)
        {
            JS.Log("_device", device);
            JS.Set("_device", device);
            if (!device.Opened)
            {
                JS.Log("Opening...");
                await device.Open();
                JS.Log("Opened.");
            }
            await device.SelectConfiguration(1);
            await device.ClaimInterface(0);
            await device.SelectAlternateInterface(0, 0);
            JS.Log("Reconfigured.");
            Device = device;
            OnConnected?.Invoke();
        }
        /// <summary>
        /// https://github.com/FlintEastwood/3DVisionActivator/blob/f193ecf7293d4c352a056044e9917b816ced39f4/src/system/nvidiaShutterGlasses.cpp#L294
        /// </summary>
        /// <returns></returns>
        public async Task ToggleEyes1(int offset = 5)
        {
            IsLeftEye = !IsLeftEye;
            var sequence = new int[] { IsLeftEye ? 0x0000feaa : 0x0000ffaa, offset };
            await writeToPipe(sequence);
        }

        /// <summary>
        /// https://github.com/bobsomers/3dvgl/blob/fb9deccb78f3ece4884da0e4ed3da316ec324a2d/lib/nvstusb.c#L326
        /// https://github.com/eruffaldi/libnvstusb/blob/master/src/nvstusb.c
        /// </summary>
        /// <param name="offset"></param>
        /// <returns></returns>
        public async Task ToggleEyes(int offset = 5)
        {
            var rate = 60;
            uint b = NVSTUSB_T2_COUNT((1000000 / rate) / 1.8d);
            IsLeftEye = !IsLeftEye;
            var leftEye = IsLeftEye;
            if (InvertEyes) leftEye = !leftEye;
            var sequence = new byte[8]
            {
                NVSTUSB_CMD_SET_EYE,
                leftEye ? (byte)0xFE : (byte)0xFF,
                0, 0, // unused
                (byte)b, (byte)(b>> 8), (byte)(b>> 16), (byte)(b>>24)
            };
            await writeToPipe(sequence);
        }
        public async Task SendInit()
        {
            //var sequence = new int[] { 0x42180300 };
            var sequence = new int[] { 0x00031842 };
            await writeToPipe(sequence);

            JS.Log("TransferIn...");
            var readResult = await Device!.TransferIn(EndpointNumber, 7);
            JS.Log("readResult", readResult);
            var readBuffer = readResult.Data!.ReadBytes();
            JS.Log("readBuffer", readBuffer);
        }
        public bool IsLeftEye { get; private set; } = false;

        async Task writeToPipe(int[] data)
        {
            if (USB == null) return;
            if (Device == null) return;
            using var pipeData = new Int32Array(data);
            var result = await Device.TransferOut(EndpointNumber, pipeData);
            JS.Log("writeToPipe", pipeData, result);
        }
        async Task writeToPipe(byte[] data)
        {
            if (USB == null) return;
            if (Device == null) return;
            var result = await Device.TransferOut(EndpointNumber, data);
            JS.Log("writeToPipe", data, result);
        }

        public bool Opened
        {
            get
            {
                var ret = false;
                try
                {
                    ret = Device != null && Device.Opened;
                }
                catch { }
                return ret;
            }
        }
        public bool Connected => Device != null;
        /// <summary>
        /// Uploads the firmware file nvstusb.fw to the connected Nvidia 3D Vision IR Emitter
        /// https://github.com/bobsomers/3dvgl/blob/fb9deccb78f3ece4884da0e4ed3da316ec324a2d/lib/usb_libusb.c#L108
        /// </summary>
        /// <param name="uploadIfNeeded"></param>
        /// <returns></returns>
        public async Task<bool> FirmwareUpdate()
        {
            if (Device == null) return false;
            var firmwareNeeded = await FirmwareCheck();
            if (firmwareNeeded)
            {
                //  firmware file nvstusb.fw
                var firmwareBytes = await HttpClient.GetByteArrayAsync("nvstusb.fw");

                // Upload firmware logic here
                JS.Log("Firmware upload needed - uploading...", firmwareBytes.Length);

                // upload the firmware in chunks
                // each chunk is prefixed with a 2-byte length and a 2-byte destination position
                var i = 0;
                while (i < firmwareBytes.Length)
                {
                    var length = BinaryPrimitives.ReadUInt16BigEndian(new ReadOnlySpan<byte>(firmwareBytes, i, 2));
                    i += 2;
                    var pos = BinaryPrimitives.ReadUInt16BigEndian(new ReadOnlySpan<byte>(firmwareBytes, i, 2));
                    i += 2;
                    Console.WriteLine($"copy {pos} {length}");
                    var chunk = new byte[length];
                    System.Array.Copy(firmwareBytes, i, chunk, 0, length);
                    i += length;
                    // Send chunk to device position pos
                    await Device.ControlTransferOut(new USBControlTransferParameters
                    {
                        RequestType = "vendor",
                        Recipient = "device",
                        Request = 0xA0, // Firmware load
                        Value = pos,    // piece destination position
                        Index = 0x0000
                    }, chunk);
                }
                try
                {
                    await Device.Reset();
                    await Task.Delay(250);
                }
                catch (Exception ex)
                {
                    JS.Log($"Reset error: {ex.Message}");
                }
                await Device.Close();
                await Task.Delay(250);
                await Device.Open();
                await Task.Delay(250);
                JS.Log("Firmware upload completed.");
                await Device.SelectConfiguration(1);
                await Device.ClaimInterface(0);
                JS.Log("Reconfigured.");
            }
            return firmwareNeeded;
        }
        /// <summary>
        /// Returns true if it appears the firmware needs to be loaded<br/>
        /// https://github.com/bobsomers/3dvgl/blob/fb9deccb78f3ece4884da0e4ed3da316ec324a2d/lib/usb_libusb.c#L96
        /// Currently always returns true because the endpoint count always returns 0... 
        /// </summary>
        /// <returns></returns>
        public async Task<bool> FirmwareCheck()
        {
            if (Device == null) return false;
            var endpointCount = await GetEndpointCount(Device);
            return endpointCount == 0;
        }
        /// <summary>
        /// https://github.com/bobsomers/3dvgl/blob/fb9deccb78f3ece4884da0e4ed3da316ec324a2d/lib/usb_libusb.c#L76C1-L76C29
        /// </summary>
        /// <param name="device"></param>
        /// <returns></returns>
        async Task<int> GetEndpointCount(USBDevice device)
        {
            var endpoints = 0;
            foreach (var config in device.Configurations)
            {
                foreach (var iface in config.Interfaces)
                {
                    foreach (var alt in iface.Alternates)
                    {
                        endpoints += alt.Endpoints.Length;
                    }
                }
            }
            return endpoints;
        }
    }
}
