using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;

[GlobalClass]
public partial class DualShockVrMapper : Node
{
	[Export] public NodePath LeftControllerPath { get; set; } = new NodePath("../XROrigin3D/LeftHand");
	[Export] public NodePath RightControllerPath { get; set; } = new NodePath("../XROrigin3D/RightHand");
	[Export] public NodePath CapturePath { get; set; } = new NodePath("../Rpcs3WindowCapture");
	[Export] public float StickDeadzone { get; set; } = 0.35f;
	[Export] public bool Enabled { get; set; } = false;

	private XRController3D _left;
	private XRController3D _right;
	private Rpcs3WindowCapture _capture;
	private readonly HashSet<ushort> _held = new();

	[DllImport("user32.dll")]
	private static extern uint MapVirtualKey(uint uCode, uint uMapType);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern uint SendInput(uint nInputs, InputRecord[] pInputs, int cbSize);

	[DllImport("user32.dll")]
	private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	private const uint WmKeydown = 0x0100;
	private const uint WmKeyup = 0x0101;

	private const int InputKeyboard = 1;
	private const uint KeyeventfKeyup = 0x0002;
	private const uint KeyeventfScancode = 0x0008;

	[StructLayout(LayoutKind.Sequential)]
	private struct InputRecord
	{
		public int Type;
		public KeyboardInput Ki;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct KeyboardInput
	{
		public ushort Vk;
		public ushort Scan;
		public uint Flags;
		public uint Time;
		public IntPtr ExtraInfo;
	}

	public override void _Ready()
	{
		_left = GetNodeOrNull<XRController3D>(LeftControllerPath);
		_right = GetNodeOrNull<XRController3D>(RightControllerPath);
		_capture = GetNodeOrNull<Rpcs3WindowCapture>(CapturePath);
	}

	public override void _Process(double delta)
	{
		if (!Enabled)
		{
			ReleaseAll();
			return;
		}

		var wanted = new HashSet<ushort>();

		if (_left != null)
		{
			AddStick(_left.GetVector2("primary"), 0x57, 0x53, 0x41, 0x44, wanted);
			if (_left.GetFloat("trigger") > 0.6f)
				wanted.Add(0x52); // L2
			if (_left.IsButtonPressed("by_button"))
				wanted.Add(0x5A); // Square
			if (_left.IsButtonPressed("ax_button"))
				wanted.Add(0x08); // PS / Backspace
			if (_left.IsButtonPressed("menu_button"))
				wanted.Add(0x20); // Select
		}

		if (_right != null)
		{
			AddStick(_right.GetVector2("primary"), 0x24, 0x23, 0x2E, 0x22, wanted);
			if (_right.GetFloat("trigger") > 0.55f)
				wanted.Add(0x58); // Cross
			if (_right.GetFloat("grip") > 0.7f && !IsHoldingPickup(_right))
				wanted.Add(0x45); // R1
			if (_right.IsButtonPressed("ax_button"))
				wanted.Add(0x43); // Circle
			if (_right.IsButtonPressed("by_button"))
				wanted.Add(0x56); // Triangle
			if (_right.IsButtonPressed("menu_button"))
				wanted.Add(0x0D); // Start
		}

		if (_left != null && _left.GetFloat("grip") > 0.7f && !IsHoldingPickup(_left))
			wanted.Add(0x51); // L1

		SyncKeys(wanted);
	}

	private static bool IsHoldingPickup(XRController3D hand)
	{
		foreach (Node child in hand.GetChildren())
		{
			Variant obj = child.Get("picked_up_object");
			if (obj.VariantType != Variant.Type.Nil && obj.AsGodotObject() != null)
				return true;
		}
		return false;
	}

	private void AddStick(Vector2 stick, ushort up, ushort down, ushort left, ushort right, HashSet<ushort> wanted)
	{
		if (stick.Y > StickDeadzone)
			wanted.Add(up);
		if (stick.Y < -StickDeadzone)
			wanted.Add(down);
		if (stick.X < -StickDeadzone)
			wanted.Add(left);
		if (stick.X > StickDeadzone)
			wanted.Add(right);
	}

	private void SyncKeys(HashSet<ushort> wanted)
	{
		foreach (ushort vk in wanted)
		{
			if (_held.Add(vk))
				SendKey(vk, false);
		}

		var toRelease = new List<ushort>();
		foreach (ushort vk in _held)
		{
			if (!wanted.Contains(vk))
				toRelease.Add(vk);
		}

		foreach (ushort vk in toRelease)
		{
			_held.Remove(vk);
			SendKey(vk, true);
		}
	}

	private void ReleaseAll()
	{
		foreach (ushort vk in _held)
			SendKey(vk, true);
		_held.Clear();
	}

	private void SendKey(ushort vk, bool up)
	{
		ushort scan = (ushort)MapVirtualKey(vk, 0);
		var input = new InputRecord
		{
			Type = InputKeyboard,
			Ki = new KeyboardInput
			{
				Vk = 0,
				Scan = scan,
				Flags = KeyeventfScancode | (up ? KeyeventfKeyup : 0)
			}
		};
		SendInput(1, new[] { input }, Marshal.SizeOf<InputRecord>());

		IntPtr hwnd = _capture?.CurrentHwnd ?? IntPtr.Zero;
		if (hwnd != IntPtr.Zero)
		{
			int lp = (scan << 16) | 1;
			if (up)
				lp |= 1 << 31;
			PostMessage(hwnd, up ? WmKeyup : WmKeydown, (IntPtr)vk, (IntPtr)lp);
		}
	}

	public override void _ExitTree()
	{
		ReleaseAll();
	}
}
