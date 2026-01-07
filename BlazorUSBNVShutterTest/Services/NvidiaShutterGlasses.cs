using SpawnDev;
using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;
using System;
using System.Buffers.Binary;

namespace BlazorUSBNVShutterTest.Services
{
    public class NvidiaShutterGlasses : IBackgroundService
    {
        BlazorJSRuntime JS;
        public USBDevice? Device;
        USB? USB = null;
        public event Action OnConnected = default!;
        public event Action OnDisconnected = default!;
        HttpClient HttpClient;
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
            //OnConnected?.Invoke();
        }
        void USB_OnDisconnect(USBConnectionEvent e)
        {
            JS.Log("USB OnDisconnect", e);
            //OnConnected?.Invoke();
        }
        public async Task FindDevices()
        {
                       if (USB == null) return;
            var devices = await USB.GetDevices();
            foreach (var device in devices)
            {
                JS.Log("Found Device", device);
                if (device.VendorId == 0x0955 && device.ProductId == 0x0007)
                {
                    Device = device;
                    await FirmwareCheck(false);
                    OnConnected?.Invoke();
                }
            }

        }
        public async Task<bool> Connect()
        {
            if (USB == null) return false;
            if (Device != null) return true; // Already connected

            USBDevice? device = null;
            try
            {
                var options = new USBDeviceRequestOptions
                {
                    Filters = new List<USBDeviceFilter>
                    {
                        new USBDeviceFilter
                        {
                            VendorId = 0x0955, // Nvidia (2389) Vendor ID
                            ProductId = 0x0007 // Product ID for shutter glasses (model I have) (7)
                        }
                    }
                };
                JS.Log("_options", options);

                device = await USB.RequestDevice(options);
                if (device == null) return false;
                await device.Open();
            }
            catch (JSException ex)
            {
                JS.Log("Device request cancelled or failed", ex.ToString());
                return false;
            }
            if (device == null) return false;
            Device = device;
            JS.Log("_device", device);
            await FirmwareCheck(true);

            OnConnected?.Invoke();

            return Device != null;
        }
        /// <summary>
        /// When plugged into USB, the Nvidia Shutter Lgasses transmitter I have blinks red.<br/>
        /// This apparently means the device is not ready to run because it needs firmware to be uploaded.<br/>
        /// This method can also upload that firmware (nvstusb.fw), which changes the transmitter light from 
        /// blinking red to solid green, indicating it is ready for operation.
        /// </summary>
        /// <param name="uploadIfNeeded"></param>
        /// <returns></returns>
        public async Task<bool> FirmwareCheck(bool uploadIfNeeded = false)
        {
            if (Device == null) return false;
            var firmwareNeeded = await FirmwareNeededCheck(Device);
            if (firmwareNeeded && uploadIfNeeded)
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
                    // Send chunk to device
                    await Device.ControlTransferOut(new USBControlTransferParameters
                    {
                        RequestType = "vendor",
                        Recipient = "device",
                        Request = 0xA0, /* 'Firmware load' */
                        Value = pos,    // piece destination position
                        Index = 0x0000
                    }, chunk);
                }
                JS.Log("Firmware upload completed.");
            }
            return true;
        }
        async Task<bool> FirmwareNeededCheck(USBDevice device)
        {
            if (Device == null) return false;
            var endpointCount = await GetEndpointCount(device);
            return endpointCount == 0;
        }
        async Task<bool> FirmwareUpload(USBDevice device)
        {
            if (Device == null) return false;
            var endpointCount = await GetEndpointCount(device);
            return endpointCount == 0;
        }
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
