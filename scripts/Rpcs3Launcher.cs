using System;
using System.Diagnostics;
using System.IO;
using Godot;

[GlobalClass]
public partial class Rpcs3Launcher : Node
{
	[Export] public string Rpcs3Exe { get; set; } = @"D:\rpcs3-v0.0.41-19558-dc06cac0_win64_msvc\rpcs3.exe";
	[Export] public string GamePath { get; set; } = "";
	[Export] public bool NoGui { get; set; } = false;
	[Export] public int IpcPort { get; set; } = 28013;

	private Process _process;

	public string Status { get; private set; } = "RPCS3 not started";

	public override void _ExitTree()
	{
		StopEmulator();
	}

	public bool StartEmulator()
	{
		if (_process != null && !_process.HasExited)
		{
			Status = "RPCS3 already running";
			return true;
		}

		Process[] existing = Process.GetProcessesByName("rpcs3");
		if (existing.Length > 0)
		{
			_process = existing[0];
			Status = "Attached to running RPCS3";
			return true;
		}

		if (string.IsNullOrWhiteSpace(Rpcs3Exe) || !File.Exists(Rpcs3Exe))
		{
			Status = "RPCS3 executable not found. Set Rpcs3Exe on the launcher node.";
			GD.PrintErr(Status);
			return false;
		}

		EnsureSkyIpcEnabled();

		var args = NoGui ? "--no-gui" : "";
		if (!string.IsNullOrWhiteSpace(GamePath))
			args = $"{args} \"{GamePath}\"".Trim();

		try
		{
			_process = Process.Start(new ProcessStartInfo
			{
				FileName = Rpcs3Exe,
				Arguments = args,
				WorkingDirectory = Path.GetDirectoryName(Rpcs3Exe) ?? "",
				UseShellExecute = true
			});
			Status = _process == null ? "Failed to start RPCS3" : "RPCS3 started";
			return _process != null;
		}
		catch (Exception ex)
		{
			Status = "RPCS3 launch failed: " + ex.Message;
			GD.PrintErr(Status);
			return false;
		}
	}

	public void StopEmulator()
	{
		try
		{
			if (_process != null && !_process.HasExited && _process.ProcessName.Equals("rpcs3", StringComparison.OrdinalIgnoreCase))
				_process.CloseMainWindow();
		}
		catch (Exception ex)
		{
			GD.PrintErr("RPCS3 stop failed: " + ex.Message);
		}
	}

	private void EnsureSkyIpcEnabled()
	{
		try
		{
			string dir = Path.Combine(Path.GetDirectoryName(Rpcs3Exe) ?? "", "config");
			Directory.CreateDirectory(dir);
			string path = Path.Combine(dir, "sky_ipc.yml");
			File.WriteAllText(path,
				"Skylanders IPC Server enabled: true" + System.Environment.NewLine
				+ "Skylanders IPC Port: " + IpcPort + System.Environment.NewLine);
		}
		catch (Exception ex)
		{
			GD.PrintErr("Could not write sky_ipc.yml: " + ex.Message);
		}
	}
}
