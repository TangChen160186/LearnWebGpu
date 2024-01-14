using Silk.NET.Core.Native;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
using Silk.NET.Windowing;
using System.Runtime.InteropServices;
using Buffer = Silk.NET.WebGPU.Buffer;

namespace _8_Buffers
{
    internal static unsafe class Program
    {
        private static IWindow window;
        private static WebGPU wgpu;
        private static Instance* instance;
        private static Surface* surface;
        private static Adapter* adapter;
        private static Device* device;
        private static Queue* queue;
        private static Buffer* buffer1;
        private static Buffer* buffer2;


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
            //requestAdapterOptions.BackendType = BackendType.Vulkan;
            requestAdapterOptions.ForceFallbackAdapter = false;
            wgpu.InstanceRequestAdapter(instance, in requestAdapterOptions, new PfnRequestAdapterCallback(
                (options, adapter1, message, _) =>
                {
                    if (options == RequestAdapterStatus.Success)
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
                (options, device1, message, _) =>
                {
                    if (options == RequestDeviceStatus.Success)
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
            queue = wgpu.DeviceGetQueue(device);
            // create buffers
            BufferDescriptor bufferDescriptor = new BufferDescriptor
            {
                MappedAtCreation = false,
                Size = 16,
                Usage = BufferUsage.CopyDst | BufferUsage.CopySrc
            };
            buffer1 = wgpu.DeviceCreateBuffer(device, in bufferDescriptor);

            BufferDescriptor bufferDescriptor2 = new BufferDescriptor
            {
                MappedAtCreation = false,
                Size = 16,
                // The BufferUsage::MapRead flag is not compatible with BufferUsage::CopySrc one, so make sure not to have both at the same time.
                Usage = BufferUsage.CopyDst | BufferUsage.MapRead
            };
            buffer2 = wgpu.DeviceCreateBuffer(device, in bufferDescriptor2);

            // transfer data to buffer1
            var data = stackalloc byte[16];
            for (byte i = 0; i < 16; i++)
                data[i] = (byte)(i + 5);
            wgpu.QueueWriteBuffer(queue, buffer1, 0, data, 16);

            // copy buffer1 to buffer2
            CommandEncoderDescriptor commandEncoderDescriptor = new CommandEncoderDescriptor();
            var encoder = wgpu.DeviceCreateCommandEncoder(device, commandEncoderDescriptor);
            wgpu.CommandEncoderCopyBufferToBuffer(encoder, buffer1, 0, buffer2, 0, 16);
            var commandBuffer = wgpu.CommandEncoderFinish(encoder, new CommandBufferDescriptor());
            wgpu.QueueSubmit(queue, 1, &commandBuffer);
            wgpu.QueueOnSubmittedWorkDone(queue,
                new PfnQueueWorkDoneCallback((_, _) => { Console.WriteLine("Command work done!"); }), null);
            // mapped buffer2
            wgpu.BufferMapAsync(buffer2,MapMode.Read,0,16,new PfnBufferMapCallback(((arg0, @void) =>
            {
                var bufferMapData = (byte*)wgpu.BufferGetConstMappedRange(buffer2, 0, 16);
                for (int i = 0; i < 16; i++)
                {
                    Console.WriteLine($"{i} = {bufferMapData[i]}");
                }
                wgpu.BufferUnmap(buffer2);
            })),null);

            

        }


        private static void WindowOnRender(double obj)
        {
            wgpu.QueueSubmit(queue,0,null);
        }

        private static void WindowOnUpdate(double obj)
        {
        }

        private static void WindowOnFramebufferResize(Vector2D<int> obj)
        {
        }

        private static void WindowOnClosing()
        {

            wgpu.BufferDestroy(buffer1);
            wgpu.BufferDestroy(buffer2);

            wgpu.BufferRelease(buffer1);
            wgpu.BufferRelease(buffer2);

            wgpu.QueueRelease(queue);
            wgpu.DeviceRelease(device);
            wgpu.AdapterRelease(adapter);
            wgpu.SurfaceRelease(surface);
            wgpu.InstanceRelease(instance);
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
    }
}