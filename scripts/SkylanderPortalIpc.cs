using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Godot;

[GlobalClass]
public partial class SkylanderPortalIpc : Node
{
	[Export] public string Host { get; set; } = "127.0.0.1";
	[Export] public int Port { get; set; } = 28013;

	public string LastError { get; private set; } = "";
	public string LastResponse { get; private set; } = "";
	public string ConnectionStatus { get; private set; } = "Portal IPC not contacted yet";

	public bool LoadFigure(int slot, string figurePath)
	{
		string path = NormalizeDumpPath(figurePath);
		if (string.IsNullOrWhiteSpace(path))
		{
			LastError = "Figure dump path is empty";
			ConnectionStatus = LastError;
			return false;
		}

		return Send($"load {slot} {path}", out _);
	}

	public bool RemoveFigure(int slot)
	{
		return Send($"remove {slot}", out _);
	}

	public bool Clear()
	{
		return Send("clear", out _);
	}

	public string Status()
	{
		return Send("status", out string body) ? body : LastError;
	}

	public List<string> ListStatusLines()
	{
		var lines = new List<string>();
		if (Send("status", out string body))
		{
			foreach (string line in body.Split('\n'))
			{
				if (!string.IsNullOrWhiteSpace(line))
					lines.Add(line.Trim());
			}
		}
		return lines;
	}

	private static string NormalizeDumpPath(string figurePath)
	{
		if (string.IsNullOrWhiteSpace(figurePath))
			return "";
		string path = figurePath.Trim().Trim('"');
		if (path.StartsWith("user://", StringComparison.OrdinalIgnoreCase)
			|| path.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
			path = ProjectSettings.GlobalizePath(path);
		path = path.Replace('/', '\\');
		return path;
	}

	private bool Send(string command, out string response)
	{
		response = "";
		LastError = "";
		LastResponse = "";
		try
		{
			using var client = new TcpClient(AddressFamily.InterNetwork);
			client.ReceiveTimeout = 1000;
			client.SendTimeout = 1000;
			client.Connect(IPAddress.Loopback, Port);
			using NetworkStream stream = client.GetStream();
			byte[] payload = Encoding.UTF8.GetBytes(command.Trim() + "\n");
			stream.Write(payload, 0, payload.Length);

			var text = new StringBuilder();
			var buffer = new byte[4096];
			do
			{
				int read = stream.Read(buffer, 0, buffer.Length);
				if (read <= 0)
					break;
				text.Append(Encoding.UTF8.GetString(buffer, 0, read));
			}
			while (stream.DataAvailable);

			response = text.ToString().Trim();
			LastResponse = response;
			if (string.IsNullOrEmpty(response))
			{
				LastError = "Portal IPC returned an empty response";
				ConnectionStatus = LastError;
				return false;
			}
			if (response.StartsWith("error", StringComparison.OrdinalIgnoreCase))
			{
				LastError = response;
				ConnectionStatus = response;
				return false;
			}

			ConnectionStatus = "Portal IPC connected";
			return true;
		}
		catch (SocketException ex)
		{
			LastError = "Portal IPC offline on 127.0.0.1:" + Port + " (" + ex.SocketErrorCode + ")";
			ConnectionStatus = LastError + ". Enable Skylanders Portal IPC in RPCS3 and restart the emulator.";
			return false;
		}
		catch (Exception ex)
		{
			LastError = "Portal IPC: " + ex.Message;
			ConnectionStatus = LastError;
			return false;
		}
	}
}
