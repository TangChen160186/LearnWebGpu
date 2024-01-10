using Silk.NET.Maths;
using Silk.NET.WebGPU;
using Silk.NET.Windowing;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;

namespace _7_HelloTriangle
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
        private static RenderPipeline* renderPipeline;
        private static ShaderModule * shaderModule;
        private static string shaderSource = """
                                             @vertex
                                             fn vs_main(@builtin(vertex_index) in_vertex_index: u32) -> @builtin(position) vec4<f32> {
                                             	var p = vec2f(0.0, 0.0);
                                             	if (in_vertex_index == 0u) {
                                             		p = vec2f(-0.5, -0.5);
                                             	} else if (in_vertex_index == 1u) {
                                             		p = vec2f(0.5, -0.5);
                                             	} else {
                                             		p = vec2f(0.0, 0.5);
                                             	}
                                             	return vec4f(p, 0.0, 1.0);
                                             }
                                             
                                             @fragment
                                             fn fs_main() -> @location(0) vec4f {
                                                 return vec4f(0.0, 0.4, 1.0, 1.0);
                                             }
                                             """;
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

            // Get Queue
            queue = wgpu.DeviceGetQueue(device);


            // create pipeline

            CreatePipeline();

            // config surface
            CreateSwapChain();

        }

        private static void CreatePipeline()
        {
            // load shaderModule
            {
                var wgslDescriptor = new ShaderModuleWGSLDescriptor
                {
                    Code = (byte*)SilkMarshal.StringToPtr(shaderSource),
                    Chain = new ChainedStruct
                    {
                        SType = SType.ShaderModuleWgslDescriptor
                    }
                };

                var shaderModuleDescriptor = new ShaderModuleDescriptor
                {
                    NextInChain = (ChainedStruct*)(&wgslDescriptor),
                };

                shaderModule = wgpu.DeviceCreateShaderModule(device, in shaderModuleDescriptor);
            }
            BlendState blendState = new BlendState()
            {
                Color = new BlendComponent
                {
                    SrcFactor = BlendFactor.SrcAlpha,
                    DstFactor = BlendFactor.OneMinusSrcAlpha,
                    Operation = BlendOperation.Add
                },
                Alpha = new BlendComponent
                {
                    SrcFactor = BlendFactor.Zero,
                    DstFactor = BlendFactor.One,
                    Operation = BlendOperation.Add
                }
            };
            ColorTargetState colorTargetState = new ColorTargetState()
            {
                Blend = &blendState,
                Format = wgpu.SurfaceGetPreferredFormat(surface, adapter),
                WriteMask = ColorWriteMask.All
            };

            FragmentState fragmentState = new FragmentState()
            {
                ConstantCount = 0,
                Constants = null,
                EntryPoint = (byte*)SilkMarshal.StringToPtr("fs_main"),
                Module = shaderModule,
                TargetCount = 1,
                Targets = &colorTargetState,
            };
            RenderPipelineDescriptor renderPipelineDescriptor = new RenderPipelineDescriptor
            {
                DepthStencil = null,
                Vertex = new VertexState()
                {
                    BufferCount = 0,
                    Buffers = null,
                    ConstantCount = 0,
                    Constants = null,
                    EntryPoint = (byte*)SilkMarshal.StringToPtr("vs_main"),
                    Module = shaderModule,
                },
                Primitive = new PrimitiveState()
                {
                    CullMode = CullMode.None,
                    FrontFace = FrontFace.Ccw,
                    StripIndexFormat = IndexFormat.Undefined,
                    Topology = PrimitiveTopology.TriangleList
                },
                Fragment = &fragmentState,
                Multisample = new MultisampleState()
                {
                    Count = 1,
                    Mask = ~0u,
                    AlphaToCoverageEnabled = false,
                },
                Label = null,
                Layout = null,
            };
            renderPipeline = wgpu.DeviceCreateRenderPipeline(device, renderPipelineDescriptor);
        }
        private static void CreateSwapChain()
        {
            SurfaceConfiguration configuration = new SurfaceConfiguration
            {
                Device = device,
                Format = wgpu.SurfaceGetPreferredFormat(surface, adapter),
                Usage = TextureUsage.RenderAttachment,
                Width = (uint)window.FramebufferSize.X,
                Height = (uint)window.FramebufferSize.Y,
                PresentMode = PresentMode.Fifo,
                AlphaMode = CompositeAlphaMode.Opaque
            };

            wgpu.SurfaceConfigure(surface, in configuration);
        }

        private static void WindowOnRender(double obj)
        {
            // Create CommandEncoder
            CommandEncoderDescriptor commandEncoderDescriptor = new CommandEncoderDescriptor();
            var encoder = wgpu.DeviceCreateCommandEncoder(device, in commandEncoderDescriptor);

            // Get texture
            SurfaceTexture surfaceTexture;
            wgpu.SurfaceGetCurrentTexture(surface, &surfaceTexture);
            switch (surfaceTexture.Status)
            {
                case SurfaceGetCurrentTextureStatus.Timeout:
                case SurfaceGetCurrentTextureStatus.Outdated:
                case SurfaceGetCurrentTextureStatus.Lost:
                    // Recreate swapchain,
                    wgpu.TextureRelease(surfaceTexture.Texture);
                    CreateSwapChain();
                    // Skip this frame
                    return;
                case SurfaceGetCurrentTextureStatus.OutOfMemory:
                case SurfaceGetCurrentTextureStatus.DeviceLost:
                case SurfaceGetCurrentTextureStatus.Force32:
                    throw new Exception($"What is going on bros... {surfaceTexture.Status}");
            }

            TextureView* view = wgpu.TextureCreateView(surfaceTexture.Texture, null);

            RenderPassColorAttachment renderPassColorAttachment = new RenderPassColorAttachment
            {
                LoadOp = LoadOp.Clear,
                ClearValue = new Color(0.9, 0.1, 0.2, 1.0),
                StoreOp = StoreOp.Store,
                View = view
            };
            RenderPassDescriptor renderPassDescriptor = new RenderPassDescriptor
            {
                ColorAttachmentCount = 1,
                ColorAttachments = &renderPassColorAttachment,
                DepthStencilAttachment = null,
                TimestampWrites = null,
                OcclusionQuerySet = null
            };
            RenderPassEncoder* renderPassEncoder = wgpu.CommandEncoderBeginRenderPass(encoder, in renderPassDescriptor);
            wgpu.RenderPassEncoderSetPipeline(renderPassEncoder,renderPipeline);
            wgpu.RenderPassEncoderDraw(renderPassEncoder,3,1,0,0);
            wgpu.RenderPassEncoderEnd(renderPassEncoder);
            // encoder you command
            CommandBufferDescriptor commandBufferDescriptor = new CommandBufferDescriptor();
            CommandBuffer* commandBuffer = wgpu.CommandEncoderFinish(encoder, commandBufferDescriptor);

            // submit queue
            wgpu.QueueSubmit(queue, 1, &commandBuffer);
            //wgpu.QueueOnSubmittedWorkDone(queue, new PfnQueueWorkDoneCallback((status, _) =>
            //{
            //    Console.WriteLine($"Queued work finished with status: {status}");
            //}), null);
            wgpu.SurfacePresent(surface);

            // release
            wgpu.CommandBufferRelease(commandBuffer);
            wgpu.RenderPassEncoderRelease(renderPassEncoder);
            wgpu.CommandEncoderRelease(encoder);
            wgpu.TextureRelease(surfaceTexture.Texture);
            wgpu.TextureViewRelease(view);

        }

        private static void WindowOnUpdate(double obj)
        {
        }

        private static void WindowOnFramebufferResize(Vector2D<int> obj)
        {
            CreateSwapChain();
        }

        private static void WindowOnClosing()
        {
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
