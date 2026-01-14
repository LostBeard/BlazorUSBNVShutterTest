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
        static uint NVSTUSB_T0_COUNT(double us) => (uint)(-us * (NVSTUSB_T0_CLOCK / 1000000L) + 1);
        const byte NVSTUSB_CMD_WRITE = 0x01;
        const byte NVSTUSB_CMD_READ = 0x02;
        const byte NVSTUSB_CMD_CLEAR = 0x40;
        const byte NVSTUSB_CMD_SET_EYE = 0xAA;
        public bool InvertEyes { get; set; }
        int EndpointNumber = -1;

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
            // Search for the first OUT endpoint in ALL configurations
            USBEndpoint? outEndpoint = null;
            USBInterface? foundInterface = null;
            USBAlternateInterface? foundAlternate = null;
            USBConfiguration? foundConfig = null;

            JS.Log("USB Configurations:", device.Configurations);

            foreach (var config in device.Configurations)
            {
                foreach (var iface in config.Interfaces)
                {
                    foreach (var alt in iface.Alternates)
                    {
                        foreach (var endpoint in alt.Endpoints)
                        {
                            if (endpoint.Direction == "out")
                            {
                                outEndpoint = endpoint;
                                foundInterface = iface;
                                foundAlternate = alt;
                                foundConfig = config;
                                break;
                            }
                        }
                        if (outEndpoint != null) break;
                    }
                    if (outEndpoint != null) break;
                }
                if (outEndpoint != null) break;
            }

            if (outEndpoint != null && foundInterface != null && foundAlternate != null && foundConfig != null)
            {
                JS.Log($"Found Endpoint: {outEndpoint.EndpointNumber} Interface: {foundInterface.InterfaceNumber} Alt: {foundAlternate.AlternateSetting} Config: {foundConfig.ConfigurationValue}");
                
                await device.SelectConfiguration(foundConfig.ConfigurationValue);
                await device.ClaimInterface(foundInterface.InterfaceNumber);
                // Only select alternate if it's not the default (0) or if we need to enforce it
                await device.SelectAlternateInterface(foundInterface.InterfaceNumber, foundAlternate.AlternateSetting);
                EndpointNumber = outEndpoint.EndpointNumber;
            }
            else
            {
                JS.Log("No endpoints found - Device may need firmware.");
                // Do NOT claim interfaces if none found. Just let it be connected so we can do FirmwareCheck/Update.
                // EndpointNumber remains -1
            }

            JS.Log($"Reconfigured. Using Endpoint: {EndpointNumber}");
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
        public async Task Initialize(float rate = 120)
        {
            if (Device == null) return;

            /* some timing voodoo */
            int frameTime = (int)(1000000.0 / rate);     /* 8.33333 ms if 120 Hz */
            int activeTime = 2080;                 /* 2.08000 ms time each eye is on*/

            uint w = NVSTUSB_T2_COUNT(4568.50);      /* 4.56800 ms */
            uint x = NVSTUSB_T0_COUNT(4774.25);      /* 4.77425 ms */
            uint y = NVSTUSB_T0_COUNT(activeTime);
            uint z = NVSTUSB_T2_COUNT(frameTime);

            var cmdTimings = new byte[] {
                NVSTUSB_CMD_WRITE,      /* write data */
                0x00,                   /* to address 0x2007 (0x2007+0x00) = ?? */
                0x18, 0x00,             /* 24 bytes follow */

                /* original: e1 29 ff ff (-54815; -55835) */
                (byte)w, (byte)(w>>8), (byte)(w>>16), (byte)(w>>24),    /* 2007: ?? some timer 2 counter, 1020 is subtracted from this
                                           *       loaded at startup with:
                                           *       0x44 0xEC 0xFE 0xFF (-70588(-1020)) */ 
                /* original: 68 b5 ff ff (-19096), 4.774 ms */
                (byte)x, (byte)(x>>8), (byte)(x>>16), (byte)(x>>24),    /* 200b: ?? counter saved at long at address 0x4f
                                           *       increased at timer 0 interrupt if bit 20h.1 
                                           *       is cleared, on overflow
                                           *       to 0 the code at 0x03c8 is executed.
                                           *       timer 0 will be started with this value
                                           *       by timer2 */

                /* original: 81 df ff ff (-8319), 2.08 ms */
                (byte)y, (byte)(y>>8), (byte)(y>>16), (byte)(y>>24),    /* 200f: ?? counter saved at long at address 0x4f, 784 is added to this
                                           *       if PD1 is set, delay until turning eye off? */

                /* wave forms to send via IR: */
                0x30,                     /* 2013: 110000 PD1=0, PD2=0: left eye off  */
                0x28,                     /* 2014: 101000 PD1=1, PD2=0: left eye on   */
                0x24,                     /* 2015: 100100 PD1=0, PD2=1: right eye off */
                0x22,                     /* 2016: 100010 PD1=1, PD2=1: right eye on  */

                /* ?? used when frameState is != 2, for toggling bits in Port B,
                 * values seem to have no influence on the glasses or infrared signals */
                0x0a,                     /* 2017: 1010 */
                0x08,                     /* 2018: 1000 */
                0x05,                     /* 2019: 0101 */
                0x04,                     /* 201a: 0100 */

                (byte)z, (byte)(z>>8), (byte)(z>>16), (byte)(z>>24)     /* 201b: timer 2 reload value */
              };
            await writeToPipe(cmdTimings);

            var cmd0x1c = new byte[] {
                NVSTUSB_CMD_WRITE,      /* write data */
                0x1c,                   /* to address 0x2023 (0x2007+0x1c) = ?? */
                0x02, 0x00,             /* 2 bytes follow */

                0x02, 0x00              /* ?? seems to be the start value of some 
                                           counter. runs up to 6, some things happen
                                           when it is lower, that will stop if when
                                           it reaches 6. could be the index to 6 byte values 
                                           at 0x17ce that are loaded into TH0*/
              };
            await writeToPipe(cmd0x1c);

            /* wait at most 2 seconds before going into idle */
            int timeout = (int)(rate * 4);

            var cmdTimeout = new byte[] {
                NVSTUSB_CMD_WRITE,      /* write data */
                0x1e,                   /* to address 0x2025 (0x2007+0x1e) = timeout */
                0x02, 0x00,             /* 2 bytes follow */

                (byte)timeout, (byte)(timeout>>8)     /* idle timeout (number of frames) */
              };
            await writeToPipe(cmdTimeout);

            var cmd0x1b = new byte[] {
                NVSTUSB_CMD_WRITE,      /* write data */
                0x1b,                   /* to address 0x2022 (0x2007+0x1b) = ?? */
                0x01, 0x00,             /* 1 byte follows */

                0x07                    /* ?? compared with byte at 0x29 in TD_Poll()
                                           bit 0-1: index to a table of 4 bytes at 0x17d4 (0x00,0x08,0x04,0x0C),
                                           PB1 is set in TD_Poll() if this index is 0, cleared otherwise
                                           bit 2:   set bool21_4, start timer 1, enable ext. int. 5
                                           bit 3:   PC1 is set to the inverted value of this bit in TD_Poll()
                                           bit 4-5: index to a table of 4 bytes at 0x2a 
                                           bit 6:   restart t0 on some conditions in TD_Poll()
                                         */
              };
            await writeToPipe(cmd0x1b);
        }
        public async Task SendInit()
        {
            await Initialize();
        }
        public bool IsLeftEye { get; private set; } = false;

        async Task writeToPipe(int[] data)
        {
            if (USB == null) return;
            if (Device == null) return;
            if (EndpointNumber < 0) throw new Exception("No valid USB endpoint found. Device may need firmware or drivers are incorrect.");
            using var pipeData = new Int32Array(data);
            var result = await Device.TransferOut(EndpointNumber, pipeData);
            JS.Log("writeToPipe", pipeData, result);
        }
        async Task writeToPipe(byte[] data)
        {
            if (USB == null) return;
            if (Device == null) return;
            if (EndpointNumber < 0) throw new Exception("No valid USB endpoint found. Device may need firmware or drivers are incorrect.");
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
                await Task.Delay(250);
                await Device.Open();
                await Task.Delay(250);
                JS.Log("Firmware upload completed.");
                await SetDevice(Device);
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
