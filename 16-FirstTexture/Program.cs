using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
using Silk.NET.Windowing;
using StbImageSharp;
using Buffer = Silk.NET.WebGPU.Buffer;

namespace _16_FirstTexture
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
        private static ShaderModule* shaderModule;

        private static Texture* texture;
        private static TextureView* textureView;
        private static Sampler* textureSampler;

        private static Texture* depthTexture;
        private static TextureView* depthTextureView;
        private static BindGroup* bindGroup;

        private static Buffer* mvpBuffer;
        private static string shaderSource = """
                                             struct MVP{
                                                model : mat4x4f,
                                                view : mat4x4f,
                                                projection: mat4x4f,
                                             };
                                             @group(0) @binding(0) var gradientTexture: texture_2d<f32>;
                                             @group(0) @binding(1) var textureSampler: sampler;
                                             @group(0) @binding(6) var<uniform> mvp: MVP;
                                             struct VertexInput {
                                                 @location(0) position: vec2f,
                                                 @location(1) texCoords: vec2f,
                                             };

                                             struct VertexOutput {
                                                 @builtin(position) position: vec4f,
                                                 @location(0) texCoords: vec2f,
                                             };

                                             @vertex
                                             fn vs_main(in: VertexInput) -> VertexOutput {
                                                 var out: VertexOutput;
                                                 out.position = mvp.projection * mvp.view *  mvp.model * vec4f(in.position, 0.0, 1.0);
                                                 out.texCoords = in.texCoords;
                                                 return out;
                                             }

                                             @fragment
                                             fn fs_main(in: VertexOutput) -> @location(0) vec4f {
                                                 return vec4f(textureSample(gradientTexture,textureSampler,in.texCoords).rgb,1.0);
                                             }
                                             """;

        private static Buffer* vertexBuffer;

        private static float[] vertexBufferData =
        {
            // x,   y,      u,   v,   
            -0.5F, -0.5F, 0F, 1F,
            +0.5F, -0.5F, 1F, 1F,
            +0.5F, +0.5F, 1F, 0F,
            -0.5F, +0.5F, 0F, 0F,
        };

        private static Buffer* indexBuffer;

        private static ushort[] indexBufferData =
        {
            0, 1, 2, // Triangle #0
            0, 2, 3 // Triangle #1
        };

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
                    Console.WriteLine($"Uncaptured device error: type: {Marshal.PtrToStringAnsi((IntPtr)message)}");
                    throw new Exception(
                        $"Uncaptured device error: type: {Marshal.PtrToStringAnsi((IntPtr)message)}");
                }), null);

            // Get Queue
            queue = wgpu.DeviceGetQueue(device);

            // create vertexBuffer 
            vertexBuffer = wgpu.DeviceCreateBuffer(device,
                new BufferDescriptor()
                {
                    Usage = BufferUsage.Vertex | BufferUsage.CopyDst,
                    Size = (ulong)(sizeof(float) * vertexBufferData.Length)
                });
            fixed (void* ptr = vertexBufferData)
            {
                wgpu.QueueWriteBuffer(queue, vertexBuffer, 0, ptr, (UIntPtr)(sizeof(float) * vertexBufferData.Length));
            }

            // create indexBuffer 
            indexBuffer = wgpu.DeviceCreateBuffer(device,
                new BufferDescriptor()
                {
                    Usage = BufferUsage.Index | BufferUsage.CopyDst,
                    Size = (ulong)(sizeof(ushort) * indexBufferData.Length)
                });
            fixed (void* ptr = indexBufferData)
            {
                wgpu.QueueWriteBuffer(queue, indexBuffer, 0, ptr, (UIntPtr)(sizeof(ushort) * indexBufferData.Length));
            }


            var swapchainFormat = wgpu.SurfaceGetPreferredFormat(surface, adapter);

            using (var stream = File.OpenRead("Resources/awesomeface.png"))
            {
                ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                // create a texture
                TextureDescriptor textureDescriptor = new TextureDescriptor()
                {
                    Dimension = TextureDimension.Dimension2D,
                    Size = new Extent3D()
                    {
                        DepthOrArrayLayers = 1,
                        Height = (uint)image.Height,
                        Width = (uint)image.Width
                    },
                    Format = TextureFormat.Rgba8Unorm,
                    MipLevelCount = 1,
                    SampleCount = 1,
                    Usage = TextureUsage.CopyDst | TextureUsage.TextureBinding,
                    ViewFormatCount = 0,
                    ViewFormats = null,
                };
                texture = wgpu.DeviceCreateTexture(device, in textureDescriptor);
                ImageCopyTexture destination = new ImageCopyTexture()
                {
                    Aspect = TextureAspect.All,
                    MipLevel = 0,
                    Origin = new Origin3D(0, 0, 0),
                    Texture = texture
                };
                TextureDataLayout dataLayout = new TextureDataLayout()
                {
                    Offset = 0,
                    BytesPerRow = (uint)(image.Data.Length / image.Height),
                    RowsPerImage = (uint)image.Height,
                };
                fixed (void* ptr = image.Data)
                {
                    wgpu.QueueWriteTexture(queue, destination, ptr, (UIntPtr)image.Data.Length, dataLayout,
                        textureDescriptor.Size);
                }

                textureView = wgpu.TextureCreateView(texture, new TextureViewDescriptor()
                {
                    Aspect = TextureAspect.All,
                    ArrayLayerCount = 1,
                    BaseArrayLayer = 0,
                    MipLevelCount = 1,
                    BaseMipLevel = 0,
                    Dimension = TextureViewDimension.Dimension2D,
                    Format = TextureFormat.Rgba8Unorm,
                });

                textureSampler = wgpu.DeviceCreateSampler(device, new SamplerDescriptor()
                {
                    AddressModeU = AddressMode.Repeat,
                    AddressModeV = AddressMode.Repeat,
                    Compare = CompareFunction.Undefined,
                    LodMinClamp = 0,
                    LodMaxClamp = 1,
                    MagFilter = FilterMode.Linear,
                    MinFilter = FilterMode.Linear,
                    MipmapFilter = MipmapFilterMode.Linear,
                    MaxAnisotropy = 1,
                });
            }

            // create pipeline
            mvpBuffer = wgpu.DeviceCreateBuffer(device, new BufferDescriptor()
            {
                MappedAtCreation = false,
                Size = (ulong)(Marshal.SizeOf<Matrix4x4>() * 3),
                Usage = BufferUsage.CopyDst | BufferUsage.Uniform
            });
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
            // create input layout

            var attributes = stackalloc[]
            {
                new VertexAttribute() { Format = VertexFormat.Float32x2, Offset = 0, ShaderLocation = 0 },
                new VertexAttribute()
                    { Format = VertexFormat.Float32x2, Offset = sizeof(float) * 2, ShaderLocation = 1 },
            };
            var vertexBufferLayout = new VertexBufferLayout()
            {
                ArrayStride = 4 * sizeof(float),
                AttributeCount = 2,
                Attributes = attributes,
                StepMode = VertexStepMode.Vertex
            };
            BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[]
            {
                new BindGroupLayoutEntry()
                {
                    Binding = 0,
                    Texture = new TextureBindingLayout()
                    {
                        Multisampled = false,
                        SampleType = TextureSampleType.Float,
                        ViewDimension = TextureViewDimension.Dimension2D,
                    },
                    Visibility = ShaderStage.Fragment,
                },
                new BindGroupLayoutEntry()
                {
                    Binding = 1,
                    Sampler = new SamplerBindingLayout()
                    {
                        Type = SamplerBindingType.Filtering,
                    },
                    Visibility = ShaderStage.Fragment,
                },
                new BindGroupLayoutEntry()
                {
                    Binding = 6,
                    Buffer = new BufferBindingLayout()
                    {
                        HasDynamicOffset = false,
                        MinBindingSize = (ulong)(Marshal.SizeOf<Matrix4x4>() * 3),
                        Type = BufferBindingType.Uniform
                    },
                    Visibility = ShaderStage.Vertex,
                },
            };

            BindGroupLayout* bindGroupLayout0 = wgpu.DeviceCreateBindGroupLayout(device, new BindGroupLayoutDescriptor()
            {
                EntryCount = 3,
                Entries = entries,
            });
            PipelineLayout* pipelineLayout = wgpu.DeviceCreatePipelineLayout(device, new PipelineLayoutDescriptor()
            {
                BindGroupLayoutCount = 1,
                BindGroupLayouts = &bindGroupLayout0,
            });
            DepthStencilState depthStencilState = new DepthStencilState()
            {
                DepthCompare = CompareFunction.Less,
                DepthWriteEnabled = true,
                Format = TextureFormat.Depth24Plus,

                StencilReadMask = 0,
                StencilWriteMask = 0,
                StencilFront = new StencilFaceState(CompareFunction.Less,StencilOperation.Keep),
                StencilBack = new StencilFaceState(CompareFunction.Less,StencilOperation.Keep)
            };
            RenderPipelineDescriptor renderPipelineDescriptor = new RenderPipelineDescriptor
            {
                Vertex = new VertexState()
                {
                    BufferCount = 1,
                    Buffers = &vertexBufferLayout,
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
                    Topology = PrimitiveTopology.TriangleStrip
                },
                DepthStencil = &depthStencilState,
                Fragment = &fragmentState,
                Multisample = new MultisampleState()
                {
                    Count = 1,
                    Mask = ~0u,
                    AlphaToCoverageEnabled = false,
                },
                Layout = pipelineLayout,
            };
            renderPipeline = wgpu.DeviceCreateRenderPipeline(device, renderPipelineDescriptor);

            BindGroupEntry* bindGroupEntries = stackalloc BindGroupEntry[]
            {
                new BindGroupEntry()
                {
                    Binding = 0,
                    TextureView = textureView,
                },
                new BindGroupEntry()
                {
                    Binding = 1,
                    Sampler = textureSampler,
                },
                new BindGroupEntry()
                {
                    Binding = 6,
                    Buffer = mvpBuffer,
                    Offset = 0,
                    Size = (ulong)(Marshal.SizeOf<Matrix4x4>() * 3),
                },
            };
            bindGroup = wgpu.DeviceCreateBindGroup(device, new BindGroupDescriptor()
            {
                EntryCount = 3,
                Entries = bindGroupEntries,
                Layout = bindGroupLayout0,
            });
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

            //create depth
            {
                TextureFormat format = TextureFormat.Depth24Plus;
                TextureDescriptor textureDescriptor = new TextureDescriptor()
                {
                    Dimension = TextureDimension.Dimension2D,
                    Size = new Extent3D()
                    {
                        DepthOrArrayLayers = 1,
                        Width = (uint)window.FramebufferSize.X,
                        Height = (uint)window.FramebufferSize.Y,
                    },
                    Format = format,
                    MipLevelCount = 1,
                    SampleCount = 1,
                    Usage = TextureUsage.RenderAttachment,
                    ViewFormatCount = 1,
                    ViewFormats = &format,
                };

                depthTexture = wgpu.DeviceCreateTexture(device, textureDescriptor);

                depthTextureView = wgpu.TextureCreateView(depthTexture, new TextureViewDescriptor()
                {
                    Aspect = TextureAspect.DepthOnly,
                    ArrayLayerCount = 1,
                    BaseArrayLayer = 0,
                    MipLevelCount = 1,
                    BaseMipLevel = 0,
                    Dimension = TextureViewDimension.Dimension2D,
                    Format = format,
                });
            }
        }

        private static float t;
        private static void WindowOnRender(double obj)
        {
            t += (float)obj * 10;
            Matrix4x4 modelMat = Matrix4x4.CreateRotationX(MathF.PI * t / 60);
            Matrix4x4 viewMat = Matrix4x4.CreateLookAt(new Vector3(0,0,10),Vector3.Zero,Vector3.UnitY);
            Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(1,(float)window.Size.X / window.Size.Y,0.1f,1000f);

            wgpu.QueueWriteBuffer(queue,mvpBuffer,0,&modelMat, (nuint)Marshal.SizeOf<Matrix4x4>());
            wgpu.QueueWriteBuffer(queue,mvpBuffer, (ulong)Marshal.SizeOf<Matrix4x4>(), &viewMat, (nuint)Marshal.SizeOf<Matrix4x4>());
            wgpu.QueueWriteBuffer(queue,mvpBuffer, (ulong)Marshal.SizeOf<Matrix4x4>() * 2, &projection, (nuint)Marshal.SizeOf<Matrix4x4>());
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

            RenderPassDepthStencilAttachment depthStencilAttachment = new RenderPassDepthStencilAttachment()
            {
                DepthClearValue = 1,
                DepthLoadOp = LoadOp.Clear,
                DepthStoreOp = StoreOp.Store,
                // we could turn off writing to the depth buffer globally here
                DepthReadOnly = false,


                StencilClearValue = 0,
                StencilLoadOp = LoadOp.Clear,
     
                StencilStoreOp = StoreOp.Store,
                StencilReadOnly = true,
                View = depthTextureView
            };

            RenderPassDescriptor renderPassDescriptor = new RenderPassDescriptor
            {
                ColorAttachmentCount = 1,
                ColorAttachments = &renderPassColorAttachment,
                DepthStencilAttachment = &depthStencilAttachment,
                TimestampWrites = null,
                OcclusionQuerySet = null
            };
            RenderPassEncoder* renderPassEncoder = wgpu.CommandEncoderBeginRenderPass(encoder, in renderPassDescriptor);
            wgpu.RenderPassEncoderSetPipeline(renderPassEncoder, renderPipeline);
            wgpu.RenderPassEncoderSetBindGroup(renderPassEncoder, 0, bindGroup, 0, null);
            wgpu.RenderPassEncoderSetVertexBuffer(renderPassEncoder, 0, vertexBuffer, 0,
                (ulong)(sizeof(float) * vertexBufferData.Length));
            wgpu.RenderPassEncoderSetIndexBuffer(renderPassEncoder, indexBuffer, IndexFormat.Uint16, 0,
                (ulong)(sizeof(ushort) * indexBufferData.Length));
            wgpu.RenderPassEncoderDrawIndexed(renderPassEncoder, (uint)indexBufferData.Length, 1, 0, 0, 0);
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
            wgpu.BufferDestroy(vertexBuffer);
            wgpu.BufferRelease(vertexBuffer);
            wgpu.RenderPipelineRelease(renderPipeline);
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