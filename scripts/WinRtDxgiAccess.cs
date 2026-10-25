using System;
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

internal static class WinRtDxgiAccess
{
	private static readonly Guid AccessIid = new("A9BBFDAD-F4B8-412C-8B15-04C43451B7B6");
	private static readonly Guid TextureIid = new("6f15aaf2-d208-4e89-9ab4-4890533c78d3");
	private static readonly Guid CaptureFrameIid = new("FA50C623-38DA-4B32-ACF3-FA9734AD800E");

	public static bool TryGetTexture(Direct3D11CaptureFrame frame, out IntPtr texture, out string error)
	{
		texture = IntPtr.Zero;
		error = "no surface";
		if (frame == null)
			return false;

		if (TryGetSurfaceFromFrame(frame, out IntPtr surfaceAbi, out error))
		{
			try
			{
				if (TryGetTextureFromInspectable(surfaceAbi, out texture, out error))
					return true;
			}
			finally
			{
				Marshal.Release(surfaceAbi);
			}
		}

		if (frame.Surface == null)
			return false;

		IntPtr inspectable = GetNativePointer(frame.Surface, out error);
		if (inspectable == IntPtr.Zero)
			return false;

		try
		{
			return TryGetTextureFromInspectable(inspectable, out texture, out error);
		}
		finally
		{
			Marshal.Release(inspectable);
		}
	}

	private static bool TryGetSurfaceFromFrame(Direct3D11CaptureFrame frame, out IntPtr surfacePtr, out string error)
	{
		surfacePtr = IntPtr.Zero;
		if (frame is not IWinRTObject winrt || !winrt.HasUnwrappableNativeObject || winrt.NativeObject == null)
		{
			error = "frame no unwrap";
			return false;
		}

		IntPtr inspectable = winrt.NativeObject.GetRef();
		if (inspectable == IntPtr.Zero)
		{
			error = "frame ptr=0";
			return false;
		}

		try
		{
			Guid frameIid = CaptureFrameIid;
			int hr = Marshal.QueryInterface(inspectable, ref frameIid, out IntPtr framePtr);
			if (hr < 0 || framePtr == IntPtr.Zero)
			{
				error = $"QI frame 0x{hr:X8}";
				return false;
			}

			try
			{
				IntPtr vtable = Marshal.ReadIntPtr(framePtr);
				IntPtr getSurfacePtr = Marshal.ReadIntPtr(vtable, IntPtr.Size * 6);
				if (getSurfacePtr == IntPtr.Zero)
				{
					error = "get_Surface=0";
					return false;
				}

				var getSurface = Marshal.GetDelegateForFunctionPointer<GetPtrPropDelegate>(getSurfacePtr);
				hr = getSurface(framePtr, out surfacePtr);
				if (hr < 0 || surfacePtr == IntPtr.Zero)
				{
					error = $"get_Surface 0x{hr:X8}";
					surfacePtr = IntPtr.Zero;
					return false;
				}

				error = "";
				return true;
			}
			finally
			{
				Marshal.Release(framePtr);
			}
		}
		catch (Exception ex)
		{
			error = "frame ABI: " + ex.Message;
			surfacePtr = IntPtr.Zero;
			return false;
		}
		finally
		{
			Marshal.Release(inspectable);
		}
	}

	private static bool TryGetTextureFromInspectable(IntPtr inspectable, out IntPtr texture, out string error)
	{
		texture = IntPtr.Zero;
		Guid accessIid = AccessIid;
		int hr = Marshal.QueryInterface(inspectable, ref accessIid, out IntPtr accessPtr);
		if (hr < 0 || accessPtr == IntPtr.Zero)
		{
			error = $"QI access 0x{hr:X8}";
			return false;
		}

		try
		{
			IntPtr vtable = Marshal.ReadIntPtr(accessPtr);
			IntPtr getInterfacePtr = Marshal.ReadIntPtr(vtable, IntPtr.Size * 3);
			if (getInterfacePtr == IntPtr.Zero)
			{
				error = "GetInterface=0";
				return false;
			}

			var getInterface = Marshal.GetDelegateForFunctionPointer<GetInterfaceDelegate>(getInterfacePtr);
			Guid textureIid = TextureIid;
			hr = getInterface(accessPtr, ref textureIid, out texture);
			if (hr < 0 || texture == IntPtr.Zero)
			{
				error = $"GetInterface 0x{hr:X8}";
				texture = IntPtr.Zero;
				return false;
			}

			error = "";
			return true;
		}
		finally
		{
			Marshal.Release(accessPtr);
		}
	}

	private static IntPtr GetNativePointer(IDirect3DSurface surface, out string error)
	{
		if (surface is IWinRTObject winrt && winrt.HasUnwrappableNativeObject && winrt.NativeObject != null)
		{
			IntPtr ptr = winrt.NativeObject.GetRef();
			if (ptr != IntPtr.Zero)
			{
				error = "";
				return ptr;
			}
		}

		try
		{
			if (ComWrappers.TryGetComInstance(surface, out IntPtr unknown) && unknown != IntPtr.Zero)
			{
				error = "";
				return unknown;
			}
		}
		catch (Exception ex)
		{
			error = "TryGetComInstance: " + ex.Message;
			return IntPtr.Zero;
		}

		error = surface is IWinRTObject o
			? (o.HasUnwrappableNativeObject ? "native ptr=0" : "no-unwrap")
			: surface.GetType().FullName + " not-IWinRTObject";
		return IntPtr.Zero;
	}

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate int GetInterfaceDelegate(IntPtr thisPtr, ref Guid iid, out IntPtr obj);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate int GetPtrPropDelegate(IntPtr thisPtr, out IntPtr value);
}
