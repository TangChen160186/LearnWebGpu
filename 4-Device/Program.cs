﻿using Silk.NET.Maths;
using Silk.NET.WebGPU;
using Silk.NET.Windowing;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;

namespace _4_Device
{
    internal static unsafe class Program
    {
        private static IWindow window;
        private static WebGPU wgpu;
        private static Instance* instance;
        private static Surface* surface;
        private static Adapter* adapter;
        private static Device* device;

        static void Main(string[] args)
        {
            var options = WindowOptions.Default;
            options.API = GraphicsAPI.None;
            options.Size = new Vector2D<int>(1280, 720);
            options.FramesPerSecond = 60;
            options.UpdatesPerSecond = 60;
            options.Position = new Vector2D<int>(100, 100);
            options.Title = "WebGPU Triangle";
            options.IsVisible = true;
            options.ShouldSwapAutomatically = false;
            options.IsContextControlDisabled = true;

            window = Window.Create(options);
            window.Load += WindowOnLoad;
            window.Update += WindowOnUpdate;
            window.Render += WindowOnRender;
            window.FramebufferResize += WindowOnFramebufferResize;
            window.Closing += WindowOnClosing;
            window.Run();
        }

        private static void WindowOnLoad()
        {
            wgpu = WebGPU.GetApi();
            // Create instance
            InstanceDescriptor instanceDescriptor = new InstanceDescriptor();
            instance = wgpu.CreateInstance(in instanceDescriptor);
            if (instance == null)
            {
                throw new Exception("Could not initialize WebGPU!");
            }

            // Create surface
            surface = window.CreateWebGPUSurface(wgpu, instance);

            // Request Adapter
            RequestAdapterOptions requestAdapterOptions = new RequestAdapterOptions();
            requestAdapterOptions.CompatibleSurface = surface;
            requestAdapterOptions.PowerPreference = PowerPreference.HighPerformance;
            requestAdapterOptions.BackendType = BackendType.Vulkan;
            requestAdapterOptions.ForceFallbackAdapter = false;
            wgpu.InstanceRequestAdapter(instance, in requestAdapterOptions, new PfnRequestAdapterCallback(
                (requestAdapterOptions, adapter1, message, arg3) =>
                {
                    if (requestAdapterOptions == RequestAdapterStatus.Success)
                    {
                        adapter = adapter1;
                    }
                    else
                    {
                        throw new Exception(
                            $"Could not get WebGPU adapter: {Marshal.PtrToStringAnsi((IntPtr)message)}");
                    }
                }), null);
            InspectingTheAdapter();

            // Request device
            DeviceDescriptor deviceDescriptor = new DeviceDescriptor();
            wgpu.AdapterRequestDevice(adapter, deviceDescriptor, new PfnRequestDeviceCallback(
                (requestAdapterOptions, device1, message, arg3) =>
                {
                    if (requestAdapterOptions == RequestDeviceStatus.Success)
                    {
                        device = device1;
                    }
                    else
                    {
                        throw new Exception($"Could not get WebGPU device:{Marshal.PtrToStringAnsi((IntPtr)message)}");
                    }
                }), null);

            wgpu.DeviceSetUncapturedErrorCallback(device,
                new PfnErrorCallback((errorType, message, @void) =>
                {
                    throw new Exception(
                        $"Uncaptured device error: type: {Marshal.PtrToStringAnsi((IntPtr)message)}");
                }), null);




        }

        private static void InspectingTheAdapter()
        {
            // features
            {
                var count = (int)wgpu.AdapterEnumerateFeatures(adapter, null);
                var features = stackalloc FeatureName[count];
                wgpu.AdapterEnumerateFeatures(adapter, features);
                for (int i = 0; i < count; i++)
                {
                    Console.WriteLine(features[i]);
                }
            }
            // properties
            {
                AdapterProperties properties = new AdapterProperties();
                wgpu.AdapterGetProperties(adapter, &properties);
                Console.WriteLine(properties.BackendType);
                Console.WriteLine(properties.AdapterType);
                Console.WriteLine(properties.DeviceID);
                Console.WriteLine(properties.VendorID);
                Console.WriteLine(Marshal.PtrToStringUTF8((IntPtr)properties.Name));
                Console.WriteLine(Marshal.PtrToStringUTF8((IntPtr)properties.Architecture));
                Console.WriteLine(Marshal.PtrToStringUTF8((IntPtr)properties.VendorName));
                Console.WriteLine(Marshal.PtrToStringUTF8((IntPtr)properties.DriverDescription));
            }

            // limits
            {
                SupportedLimits limits = new SupportedLimits();
                wgpu.AdapterGetLimits(adapter, &limits);
            }
        }

        private static void WindowOnRender(double obj)
        {
        }

        private static void WindowOnUpdate(double obj)
        {
        }

        private static void WindowOnFramebufferResize(Vector2D<int> obj)
        {
        }

        private static void WindowOnClosing()
        {
            wgpu.DeviceRelease(device);
            wgpu.AdapterRelease(adapter);
            wgpu.SurfaceRelease(surface);
            wgpu.InstanceRelease(instance);
        }
    }
}