# BlazorUSBNVShutterTest
This Blazor WASM app demonstrates WebUSB in Blazor using the Nvidia 3D Vision shutter glasses.

## References
- [SpawnDev.BlazorJS](https://github.com/LostBeard/SpawnDev.BlazorJS) - A Blazor library for JavaScript interop, including WebUSB support.
- [WebUSB API](https://developer.mozilla.org/en-US/docs/Web/API/WebUSB_API) - WebUSB documentation on MDN.
- [Nvidia 3D Vision - Wikipedia](https://en.wikipedia.org/wiki/Nvidia_3D_Vision) - Wikipedia article about Nvidia 3D Vision.
- [GitHub/bobsomers/3dvgl](https://github.com/bobsomers/3dvgl/blob/master/lib/nvstusb.c) - C code for interfacing with Nvidia 3D Vision USB transmitter.
- [GitHub/FlintEastwood/3DVisionActivator](https://github.com/FlintEastwood/3DVisionActivator/blob/main/src/system/nvidiaShutterGlasses.cpp) - C++ code for interfacing with Nvidia 3D Vision USB transmitter.

## Demo
[Live Demo](https://lostbeard.github.io/BlazorUSBNVShutterTest/)

### What the demo does
- When plugged into USB, the button on the front of the Nvidia 3D Vision USB transmitter blinks red, indicating the device needs firmware before it can be used. 
- The firmware file `nvstusb.fw` will be loaded onto the device using WebUSB which causes the button to become solid green, indicating it is ready for operation.
- Read the wheel and button input from the transmitter
- *Once the firmware is loaded, clicking the "Toggle Shutters" button will alternately turn the shutter glasses on and off by sending the appropriate command to the USB transmitter.

*Indicates incomplete functionality as of this writing.

### Notes
- This demo requires a browser that supports WebUSB.
- Windows may require using [Zadig](https://zadig.akeo.ie/) to load Micrsooft's WinUSB driver for the 'NVIDIA stereo controller'
- Chrome Windows - Endpoints are never found.
- Chrome Android - Endpoints are found. Transmitter front button input reading works. Wheel reading may work.

## Hardware
NVIDIA 3D Vision USB IR Emitter:
- VendorId 0x0955 (2389) - USB Vendor ID Nvidia
- ProductId 0x0007 (7) - USB Product ID for the 3D Vision IR Transmitter (this model)

![NVIDIA 3D Vision USB IR Emitter](https://github.com/LostBeard/BlazorUSBNVShutterTest/blob/53e5815a23ab82d27199a63922350d60523ffc72/BlazorUSBNVShutterTest/wwwroot/media/nvidia_3d_vision_ir_emitter_front-300x225.jpg?raw=true)

