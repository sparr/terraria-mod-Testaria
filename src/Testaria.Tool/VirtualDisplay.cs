using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Testaria.Tool;

/// <summary>
/// A framebuffer for a client process to draw into, on a machine with no
/// screen.
/// <para/>
/// A server does not need one: <c>SDL_VIDEODRIVER=dummy</c> is enough, and the
/// tool uses it. A client is not the same case, and this is measured rather
/// than assumed: with the dummy driver a client exits within five seconds of
/// starting. It wants a real X display, so on Linux the harness provides one
/// with <c>Xvfb</c>.
/// </summary>
public sealed class VirtualDisplay : IDisposable
{
	private readonly Process? xvfb;

	private VirtualDisplay(string name, Process? xvfb)
	{
		Name = name;
		this.xvfb = xvfb;
	}

	/// <summary>The display to hand a client, as <c>:n</c> or whatever DISPLAY already said.</summary>
	public string Name { get; }

	/// <summary>
	/// Provides a display, starting a virtual one where that is both needed and
	/// possible.
	/// </summary>
	/// <exception cref="HarnessException">No display, and none can be started.</exception>
	public static VirtualDisplay Provide()
	{
		// Anywhere but Linux, a desktop session is the normal case and a client
		// can use it. Windows and macOS have no Xvfb to start either.
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			return new VirtualDisplay(Environment.GetEnvironmentVariable("DISPLAY") ?? string.Empty, null);

		string? existing = Environment.GetEnvironmentVariable("DISPLAY");

		// A virtual one even when a desktop is present. A test run has no
		// business opening windows on somebody's screen while they work, and
		// an automated client would steal focus and input if it did.
		if (Which("Xvfb") is string xvfbPath)
			return Start(xvfbPath);

		if (!string.IsNullOrEmpty(existing))
			return new VirtualDisplay(existing, null);

		throw new HarnessException(
			"A test client needs a display and there is none. Install Xvfb (package 'xorg-server-xvfb' or "
			+ "'xvfb'), or run where DISPLAY is set. A server-only run needs neither.");
	}

	private static VirtualDisplay Start(string xvfbPath)
	{
		// Numbers well above what a desktop session uses, so this cannot
		// collide with a display someone is actually looking at.
		for (int number = 90; number < 160; number++) {
			string name = ":" + number;

			if (File.Exists($"/tmp/.X11-unix/X{number}"))
				continue;

			var process = Process.Start(new ProcessStartInfo(xvfbPath) {
				ArgumentList = { name, "-screen", "0", "800x600x24", "-nolisten", "tcp" },
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
			});

			if (process is null)
				continue;

			// Xvfb takes a moment to create its socket, and a client that
			// starts first simply fails to connect to it.
			for (int waited = 0; waited < 50; waited++) {
				if (File.Exists($"/tmp/.X11-unix/X{number}"))
					return new VirtualDisplay(name, process);

				if (process.HasExited)
					break;

				Thread.Sleep(100);
			}

			if (!process.HasExited)
				process.Kill();
		}

		throw new HarnessException("Could not start Xvfb on any display between :90 and :159.");
	}

	private static string? Which(string command)
	{
		foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator)) {
			string candidate = Path.Combine(directory, command);

			if (File.Exists(candidate))
				return candidate;
		}

		return null;
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		if (xvfb is null || xvfb.HasExited)
			return;

		try {
			xvfb.Kill(entireProcessTree: true);
		}
		catch (InvalidOperationException) {
			// Exited between the check and the kill, which is fine.
		}
	}
}
