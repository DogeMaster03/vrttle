using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Godot;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Foundation;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WinRT;

[GlobalClass]
public partial class Rpcs3WindowCapture : Node
{
	[Export] public NodePath TargetMeshPath { get; set; } = new NodePath("../TV/Screen");
	[Export] public int MaxWidth { get; set; } = 960;
	[Export] public float CaptureFps { get; set; } = 60.0f;

	public string CaptureStatus { get; private set; } = "Waiting for RPCS3 window";
	public IntPtr CurrentHwnd => _hwnd;

	private MeshInstance3D _mesh;
	private StandardMaterial3D _material;
	private ImageTexture _texture;
	private Image _image;
	private double _findAccum;
	private IntPtr _hwnd;
	private IDirect3DDevice _winrtDevice;
	private ID3D11Device _d3dDevice;
	private ID3D11DeviceContext _d3dContext;
	private ID3D11Texture2D _staging;
	private int _stagingW;
	private int _stagingH;
	private GraphicsCaptureItem _item;
	private Direct3D11CaptureFramePool _framePool;
	private GraphicsCaptureSession _session;
	private bool _wgcFailed;
	private bool _gpuCopyDisabled;
	private readonly object _wgcLock = new();
	private readonly object _frameLock = new();
	private readonly object _d3dLock = new();
	private byte[] _pendingRgba;
	private int _pendingW;
	private int _pendingH;
	private bool _pendingReady;
	private long _lastCaptureTicks;
	private string _hwndTitle = "";

	public override void _Ready()
	{
		_mesh = GetNodeOrNull<MeshInstance3D>(TargetMeshPath)
			?? GetParent()?.GetNodeOrNull<MeshInstance3D>("TV/Screen");
		_texture = new ImageTexture();
		_material = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			AlbedoColor = Colors.White
		};
		var placeholder = Image.CreateEmpty(32, 32, false, Image.Format.Rgba8);
		placeholder.Fill(new Color(0.04f, 0.05f, 0.08f));
		_texture.SetImage(placeholder);
		_material.AlbedoTexture = _texture;
		if (_mesh != null)
		{
			_mesh.MaterialOverride = _material;
			_mesh.SetSurfaceOverrideMaterial(0, _material);
		}
		else
			GD.PrintErr("TV Screen mesh not found; capture has nowhere to draw.");
		TryCreateWinrtDevice();
	}

	public override void _Process(double delta)
	{
		UploadPendingFrame();

		_findAccum += delta;
		if (_findAccum < 0.75)
			return;
		_findAccum = 0;

		IntPtr next = FindRpcs3Window();
		if (next == IntPtr.Zero)
		{
			if (_hwnd != IntPtr.Zero)
				StopWgc();
			_hwnd = IntPtr.Zero;
			_hwndTitle = "";
			CaptureStatus = "No RPCS3 window yet — boot a game in RPCS3 (not just the game list)";
			return;
		}

		if (next == _hwnd && IsWindow(_hwnd) && IsWindowVisible(_hwnd))
		{
			if (!_wgcFailed && _session == null)
				StartWgc();
			return;
		}

		StopWgc();
		_hwnd = next;
		_hwndTitle = GetWindowTitle(_hwnd);
		_wgcFailed = false;
		_gpuCopyDisabled = false;
		CaptureStatus = $"Found \"{_hwndTitle}\", starting capture";
		StartWgc();
	}

	public override void _ExitTree()
	{
		StopWgc();
		lock (_d3dLock)
		{
			_staging?.Dispose();
			_staging = null;
			_d3dContext?.Dispose();
			_d3dContext = null;
			_d3dDevice?.Dispose();
			_d3dDevice = null;
		}
		_winrtDevice = null;
	}

	private void TryCreateWinrtDevice()
	{
		try
		{
			Result result = D3D11.D3D11CreateDevice(
				null,
				DriverType.Hardware,
				DeviceCreationFlags.BgraSupport,
				[FeatureLevel.Level_11_0],
				out _d3dDevice,
				out _d3dContext);
			if (result.Failure || _d3dDevice == null)
			{
				CaptureStatus = "D3D11 device failed for window capture";
				_wgcFailed = true;
				return;
			}

			using IDXGIDevice dxgiDevice = _d3dDevice.QueryInterface<IDXGIDevice>();
			int hr = (int)CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out IntPtr inspectable);
			if (hr < 0 || inspectable == IntPtr.Zero)
			{
				_wgcFailed = true;
				return;
			}

			_winrtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
			Marshal.Release(inspectable);
		}
		catch (Exception ex)
		{
			GD.PrintErr("WinRT capture device failed: " + ex.Message);
			_wgcFailed = true;
		}
	}

	private void StartWgc()
	{
		if (_winrtDevice == null || _hwnd == IntPtr.Zero)
		{
			_wgcFailed = true;
			return;
		}

		lock (_wgcLock)
		{
			if (_session != null || _wgcFailed)
				return;
			try
			{
				_item = CreateItemForWindow(_hwnd);
				if (_item == null)
				{
					IntPtr owner = GetWindow(_hwnd, 4);
					if (owner != IntPtr.Zero)
						_item = CreateItemForWindow(owner);
				}
				if (_item == null)
				{
					_wgcFailed = true;
					CaptureStatus = "Graphics Capture cannot bind this window";
					return;
				}

				_framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
					_winrtDevice,
					DirectXPixelFormat.B8G8R8A8UIntNormalized,
					2,
					_item.Size);
				_framePool.FrameArrived += OnFrameArrived;
				_session = _framePool.CreateCaptureSession(_item);
				_session.IsCursorCaptureEnabled = false;
				_session.StartCapture();
				CaptureStatus = $"Capturing \"{_hwndTitle}\" {_item.Size.Width}x{_item.Size.Height}";
			}
			catch (Exception ex)
			{
				GD.PrintErr("Graphics Capture start failed: " + ex.Message);
				CaptureStatus = "Graphics Capture failed: " + ex.Message;
				_wgcFailed = true;
			}
		}

		if (_wgcFailed)
			StopWgc();
	}

	private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
	{
		if (!System.Threading.Monitor.TryEnter(_wgcLock))
		{
			sender.TryGetNextFrame()?.Dispose();
			return;
		}

		try
		{
			if (_session == null || _framePool == null)
			{
				sender.TryGetNextFrame()?.Dispose();
				return;
			}

			long minTicks = (long)(TimeSpan.TicksPerSecond / Math.Max(1.0, CaptureFps));
			long now = DateTime.UtcNow.Ticks;
			if (now - _lastCaptureTicks < minTicks)
			{
				sender.TryGetNextFrame()?.Dispose();
				return;
			}
			_lastCaptureTicks = now;

			using Direct3D11CaptureFrame frame = sender.TryGetNextFrame();
			if (frame == null)
				return;

			byte[] bgra;
			int width;
			int height;
			string copyError;

			if (!_gpuCopyDisabled)
			{
				if (TryCopySurface(frame, out bgra, out width, out height, out copyError))
				{
					QueueConvertedFrame(bgra, width, height);
					CaptureStatus = $"Capturing \"{_hwndTitle}\" {width}x{height}";
					return;
				}
				_gpuCopyDisabled = true;
			}

			if (TryCopySoftwareBitmap(frame.Surface, out bgra, out width, out height, out copyError))
			{
				QueueConvertedFrame(bgra, width, height);
				CaptureStatus = $"Capturing \"{_hwndTitle}\" {width}x{height}";
				return;
			}

			if (TryCopyWindowGdi(out bgra, out width, out height))
			{
				QueueConvertedFrame(bgra, width, height);
				CaptureStatus = $"GDI fallback {width}x{height} ({copyError})";
				return;
			}

			CaptureStatus = "GPU frame copy failed: " + copyError;
		}
		catch (Exception ex)
		{
			_gpuCopyDisabled = true;
			CaptureStatus = "Frame copy failed: " + ex.Message;
		}
		finally
		{
			System.Threading.Monitor.Exit(_wgcLock);
		}
	}

	private void QueueConvertedFrame(byte[] bgra, int width, int height)
	{
		int scale = width > MaxWidth ? (int)Math.Ceiling(width / (double)MaxWidth) : 1;
		int outW = Math.Max(16, width / scale);
		int outH = Math.Max(16, height / scale);
		byte[] rgba = new byte[outW * outH * 4];
		for (int y = 0; y < outH; y++)
		{
			int srcY = Math.Min(height - 1, y * scale);
			int srcRow = srcY * width;
			int dstRow = y * outW;
			for (int x = 0; x < outW; x++)
			{
				int si = (srcRow + Math.Min(width - 1, x * scale)) * 4;
				int di = (dstRow + x) * 4;
				rgba[di] = bgra[si + 2];
				rgba[di + 1] = bgra[si + 1];
				rgba[di + 2] = bgra[si];
				rgba[di + 3] = 255;
			}
		}

		lock (_frameLock)
		{
			_pendingRgba = rgba;
			_pendingW = outW;
			_pendingH = outH;
			_pendingReady = true;
		}
	}

	private void UploadPendingFrame()
	{
		byte[] rgba;
		int outW;
		int outH;
		lock (_frameLock)
		{
			if (!_pendingReady || _pendingRgba == null)
				return;
			rgba = _pendingRgba;
			outW = _pendingW;
			outH = _pendingH;
			_pendingReady = false;
		}

		if (_image == null || _image.GetWidth() != outW || _image.GetHeight() != outH)
		{
			_image = Image.CreateFromData(outW, outH, false, Image.Format.Rgba8, rgba);
			_texture.SetImage(_image);
			_material.AlbedoTexture = _texture;
		}
		else
		{
			_image.SetData(outW, outH, false, Image.Format.Rgba8, rgba);
			_texture.Update(_image);
		}
	}

	private bool TryCopySurface(Direct3D11CaptureFrame frame, out byte[] bgra, out int width, out int height, out string error)
	{
		bgra = null;
		width = 0;
		height = 0;
		error = "unknown";
		if (_d3dDevice == null || _d3dContext == null)
		{
			error = "no D3D device";
			return false;
		}

		if (!WinRtDxgiAccess.TryGetTexture(frame, out IntPtr texPtr, out error) || texPtr == IntPtr.Zero)
			return false;

		try
		{
			using var source = new ID3D11Texture2D(texPtr);
			Texture2DDescription desc = source.Description;
			width = (int)desc.Width;
			height = (int)desc.Height;
			if (width < 1 || height < 1)
			{
				error = "empty texture";
				return false;
			}

			bgra = new byte[width * height * 4];
			lock (_d3dLock)
			{
				if (_staging == null || _stagingW != width || _stagingH != height)
				{
					_staging?.Dispose();
					_staging = _d3dDevice.CreateTexture2D(new Texture2DDescription
					{
						Width = (uint)width,
						Height = (uint)height,
						MipLevels = 1,
						ArraySize = 1,
						Format = desc.Format == Format.Unknown ? Format.B8G8R8A8_UNorm : desc.Format,
						SampleDescription = new SampleDescription(1, 0),
						Usage = ResourceUsage.Staging,
						BindFlags = BindFlags.None,
						CPUAccessFlags = CpuAccessFlags.Read
					});
					_stagingW = width;
					_stagingH = height;
				}

				_d3dContext.CopyResource(_staging, source);
				MappedSubresource mapped = _d3dContext.Map(_staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
				try
				{
					int dstStride = width * 4;
					unsafe
					{
						byte* src = (byte*)mapped.DataPointer;
						fixed (byte* dst = bgra)
						{
							for (int y = 0; y < height; y++)
								System.Buffer.MemoryCopy(src + y * mapped.RowPitch, dst + y * dstStride, dstStride, dstStride);
						}
					}
				}
				finally
				{
					_d3dContext.Unmap(_staging, 0);
				}
			}
			error = "";
			return true;
		}
		catch (Exception ex)
		{
			error = ex.GetType().Name + ": " + ex.Message;
			return false;
		}
	}

	private static bool TryCopySoftwareBitmap(IDirect3DSurface surface, out byte[] bgra, out int width, out int height, out string error)
	{
		bgra = null;
		width = 0;
		height = 0;
		error = "SoftwareBitmap";
		if (surface == null)
			return false;

		try
		{
			IAsyncOperation<SoftwareBitmap> op = SoftwareBitmap.CreateCopyFromSurfaceAsync(surface);
			using var done = new System.Threading.ManualResetEvent(false);
			op.Completed = (_, _) =>
			{
				try { done.Set(); } catch { }
			};
			if (!done.WaitOne(80))
			{
				error = "SoftwareBitmap timeout";
				return false;
			}
			if (op.Status != AsyncStatus.Completed)
			{
				error = "SoftwareBitmap " + op.Status;
				return false;
			}

			SoftwareBitmap bitmap = op.GetResults();
			if (bitmap == null)
			{
				error = "SoftwareBitmap null";
				return false;
			}

			using (bitmap)
			{
				SoftwareBitmap bgraBitmap = bitmap;
				bool converted = false;
				if (bitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8)
				{
					bgraBitmap = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8);
					converted = true;
				}

				try
				{
					width = bgraBitmap.PixelWidth;
					height = bgraBitmap.PixelHeight;
					if (width < 1 || height < 1)
					{
						error = "SoftwareBitmap empty";
						return false;
					}

					uint nbytes = (uint)(width * height * 4);
					var buffer = new Windows.Storage.Streams.Buffer(nbytes);
					buffer.Length = nbytes;
					bgraBitmap.CopyToBuffer(buffer);
					var reader = DataReader.FromBuffer(buffer);
					bgra = new byte[nbytes];
					reader.ReadBytes(bgra);
					error = "";
					return true;
				}
				finally
				{
					if (converted)
						bgraBitmap.Dispose();
				}
			}
		}
		catch (Exception ex)
		{
			error = "SoftwareBitmap: " + ex.Message;
			return false;
		}
	}

	private bool TryCopyWindowGdi(out byte[] bgra, out int width, out int height)
	{
		bgra = null;
		width = 0;
		height = 0;
		if (_hwnd == IntPtr.Zero || !GetClientRect(_hwnd, out Rect rect))
			return false;
		width = rect.Right - rect.Left;
		height = rect.Bottom - rect.Top;
		if (width < 16 || height < 16)
			return false;

		IntPtr windowDc = GetDC(_hwnd);
		IntPtr memDc = CreateCompatibleDC(windowDc);
		IntPtr bmp = CreateCompatibleBitmap(windowDc, width, height);
		IntPtr old = SelectObject(memDc, bmp);
		if (!PrintWindow(_hwnd, memDc, 0x00000002))
			PrintWindow(_hwnd, memDc, 0);

		var info = new BitmapInfo();
		info.BmiHeader.BiSize = (uint)Marshal.SizeOf<BitmapInfoHeader>();
		info.BmiHeader.BiWidth = width;
		info.BmiHeader.BiHeight = -height;
		info.BmiHeader.BiPlanes = 1;
		info.BmiHeader.BiBitCount = 32;
		info.BmiHeader.BiCompression = 0;
		bgra = new byte[width * height * 4];
		GetDIBits(memDc, bmp, 0, (uint)height, bgra, ref info, 0);
		SelectObject(memDc, old);
		DeleteObject(bmp);
		DeleteDC(memDc);
		ReleaseDC(_hwnd, windowDc);
		return true;
	}

	private static GraphicsCaptureItem CreateItemForWindow(IntPtr hwnd)
	{
		IObjectReference factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
		Guid interopIid = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
		int hr = Marshal.QueryInterface(factory.ThisPtr, ref interopIid, out IntPtr interopPtr);
		if (hr < 0 || interopPtr == IntPtr.Zero)
			return null;

		try
		{
			IntPtr vtable = Marshal.ReadIntPtr(interopPtr);
			IntPtr createPtr = Marshal.ReadIntPtr(vtable, IntPtr.Size * 3);
			var createForWindow = Marshal.GetDelegateForFunctionPointer<CreateForWindowDelegate>(createPtr);
			Guid itemIid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
			hr = createForWindow(interopPtr, hwnd, ref itemIid, out IntPtr unk);
			if (hr < 0 || unk == IntPtr.Zero)
				return null;
			GraphicsCaptureItem item = MarshalInterface<GraphicsCaptureItem>.FromAbi(unk);
			Marshal.Release(unk);
			return item;
		}
		finally
		{
			Marshal.Release(interopPtr);
		}
	}

	private void StopWgc()
	{
		lock (_wgcLock)
		{
			try
			{
				if (_framePool != null)
					_framePool.FrameArrived -= OnFrameArrived;
			}
			catch { }
			try { _session?.Dispose(); } catch { }
			try { _framePool?.Dispose(); } catch { }
			_session = null;
			_framePool = null;
			_item = null;
		}
	}

	private static IntPtr FindRpcs3Window()
	{
		var pids = new System.Collections.Generic.HashSet<uint>();
		foreach (Process process in Process.GetProcessesByName("rpcs3"))
		{
			try { pids.Add((uint)process.Id); }
			catch { }
		}
		if (pids.Count == 0)
			return IntPtr.Zero;

		IntPtr best = IntPtr.Zero;
		int bestScore = int.MinValue;
		EnumWindows((hWnd, _) =>
		{
			GetWindowThreadProcessId(hWnd, out uint windowPid);
			if (!pids.Contains(windowPid))
				return true;
			int score = ScoreCaptureWindow(hWnd);
			if (score > bestScore)
			{
				bestScore = score;
				best = hWnd;
			}
			return true;
		}, IntPtr.Zero);
		return best;
	}

	private static int ScoreCaptureWindow(IntPtr hwnd)
	{
		if (hwnd == IntPtr.Zero || !IsWindow(hwnd) || !IsWindowVisible(hwnd) || IsIconic(hwnd))
			return int.MinValue;
		if (!GetClientRect(hwnd, out Rect rect))
			return int.MinValue;
		int width = rect.Right - rect.Left;
		int height = rect.Bottom - rect.Top;
		if (width < 160 || height < 120)
			return int.MinValue;

		string title = GetWindowTitle(hwnd);
		string lower = title.ToLowerInvariant();
		if (lower.Contains("debugger") || lower.Contains("kernel explorer")
			|| lower.Contains("pad settings") || lower.Contains("about rpcs3")
			|| lower.Contains("log viewer"))
			return int.MinValue;

		int score = width * height / 100;
		if (title == "RPCS3" || lower == "rpcs3")
			score -= 80000;
		else if (lower.StartsWith("rpcs3") && !lower.Contains("fps"))
			score -= 40000;

		if (lower.Contains("fps:") || lower.Contains("fps ") || lower.Contains("| rsx") || lower.Contains(" rsx:"))
			score += 200000;
		if (lower.Contains("skylander") || lower.Contains("spyro") || lower.Contains("giants")
			|| lower.Contains("swap force") || lower.Contains("superchargers")
			|| lower.Contains("imaginators") || lower.Contains("lego dimensions")
			|| lower.Contains("blus") || lower.Contains("bles") || lower.Contains("npub"))
			score += 150000;

		if (height > 0)
		{
			float aspect = width / (float)height;
			if (aspect > 1.4f && aspect < 2.4f)
				score += 8000;
		}

		return score;
	}

	private static string GetWindowTitle(IntPtr hwnd)
	{
		int length = GetWindowTextLength(hwnd);
		if (length <= 0)
			return "";
		var text = new StringBuilder(length + 1);
		_ = GetWindowText(hwnd, text, text.Capacity);
		return text.ToString();
	}

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate int CreateForWindowDelegate(IntPtr thisPtr, IntPtr window, ref Guid iid, out IntPtr result);

	[DllImport("user32.dll")]
	private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

	[DllImport("user32.dll")]
	private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

	[DllImport("user32.dll")]
	private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

	[DllImport("user32.dll")]
	private static extern bool IsWindow(IntPtr hWnd);

	[DllImport("user32.dll")]
	private static extern bool IsWindowVisible(IntPtr hWnd);

	[DllImport("user32.dll")]
	private static extern bool IsIconic(IntPtr hWnd);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetWindowTextLength(IntPtr hWnd);

	[DllImport("user32.dll")]
	private static extern bool GetClientRect(IntPtr hWnd, out Rect lpRect);

	[DllImport("user32.dll")]
	private static extern IntPtr GetDC(IntPtr hWnd);

	[DllImport("user32.dll")]
	private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

	[DllImport("user32.dll")]
	private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

	[DllImport("gdi32.dll")]
	private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

	[DllImport("gdi32.dll")]
	private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

	[DllImport("gdi32.dll")]
	private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

	[DllImport("gdi32.dll")]
	private static extern bool DeleteObject(IntPtr ho);

	[DllImport("gdi32.dll")]
	private static extern bool DeleteDC(IntPtr hdc);

	[DllImport("gdi32.dll")]
	private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines, byte[] lpvBits, ref BitmapInfo lpbmi, uint uUsage);

	[DllImport("d3d11.dll", ExactSpelling = true)]
	private static extern uint CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

	private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

	[StructLayout(LayoutKind.Sequential)]
	private struct Rect
	{
		public int Left, Top, Right, Bottom;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct BitmapInfoHeader
	{
		public uint BiSize;
		public int BiWidth;
		public int BiHeight;
		public ushort BiPlanes;
		public ushort BiBitCount;
		public uint BiCompression;
		public uint BiSizeImage;
		public int BiXPelsPerMeter;
		public int BiYPelsPerMeter;
		public uint BiClrUsed;
		public uint BiClrImportant;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct BitmapInfo
	{
		public BitmapInfoHeader BmiHeader;
	}
}
