# BlazorUSBNVShutterTest
This Blazor WASM app demonstrates WebUSB in Blazor using the Nvidia 3D Vision shutter glasses.

## References
- [WebUSB API](https://developer.mozilla.org/en-US/docs/Web/API/WebUSB_API) - WebUSB documentation on MDN.
- [Nvidia 3D Vision - Wikipedia](https://en.wikipedia.org/wiki/Nvidia_3D_Vision) - Wikipedia article about Nvidia 3D Vision.
- [SpawnDev.BlazorJS](https://github.com/LostBeard/SpawnDev.BlazorJS) - A Blazor library for JavaScript interop, including WebUSB support.
- [GitHub/bobsomers/3dvgl](https://github.com/bobsomers/3dvgl/blob/master/lib/nvstusb.c) - C code for interfacing with Nvidia 3D Vision USB transmitter.
- [GitHub/FlintEastwood/3DVisionActivator](https://github.com/FlintEastwood/3DVisionActivator/blob/main/src/system/nvidiaShutterGlasses.cpp) - C++ code for interfacing with Nvidia 3D Vision USB transmitter.

## What the demo does
- When plugged into USB, the button on the front of the Nvidia 3D Vision USB transmitter blinks red, indicating the device needs firmware before it can be used. 
- The firmware file `nvstusb.fw` will be loaded onto the device using WebUSB which causes the button to become solid green, indicating it is ready for operation.
- *Once the firmware is loaded, clicking the "Toggle Shutters" button will alternately turn the shutter glasses on and off by sending the appropriate command to the USB transmitter.

* Indicates incomplete functionality as of this writing.
