using System.ComponentModel;
using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;

namespace SidebarSorter;

internal static class IndependentLaunch
{
    public const string RelaunchArgument = "--independent-instance";

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsProcessInJob(IntPtr process, IntPtr job, out bool result);

    public static bool IsInJob()
    {
        using Process current = Process.GetCurrentProcess();
        if (!IsProcessInJob(current.Handle, IntPtr.Zero, out bool result))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return result;
    }

    public static int? HandoffIfNeeded(string[] arguments, bool relaunched)
    {
        if (arguments.Length == 1 && arguments[0] == "--exit") return null;
        if (!IsInJob()) return null;
        if (relaunched)
            throw new InvalidOperationException(Localization.Text("独立启动尚未就绪，请从桌面快捷方式或资源管理器打开工具。"));

        DateTime launchedAt = DateTime.UtcNow;
        int processId = LaunchThroughWindows(arguments);
        if (arguments.Length <= 1) return 0;

        try
        {
            using Process child = Process.GetProcessById(processId);
            if (!child.WaitForExit(120000)) throw new TimeoutException(Localization.Text("工具核验仍在独立进程中执行。"));
            return ReadDiagnosticResult(arguments, launchedAt);
        }
        catch (ArgumentException)
        {
            return ReadDiagnosticResult(arguments, launchedAt);
        }
    }

    private static int ReadDiagnosticResult(string[] arguments, DateTime launchedAt)
    {
        if (arguments.Length != 2 || !File.Exists(arguments[1]) || File.GetLastWriteTimeUtc(arguments[1]) < launchedAt)
            return 1;
        if (arguments[0] == "--capture") return 0;
        return JsonNode.Parse(File.ReadAllText(arguments[1]))?["success"]?.GetValue<bool>() == true ? 0 : 1;
    }

    private static int LaunchThroughWindows(string[] arguments)
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException(Localization.Text("程序入口路径不可读取。"));
        string commandLine = string.Join(" ", new[] { executable }.Concat(arguments).Append(RelaunchArgument).Select(QuoteArgument));
        Type locatorType = Type.GetTypeFromProgID("WbemScripting.SWbemLocator")
            ?? throw new InvalidOperationException(Localization.Text("Windows 独立启动服务不可用，请从桌面快捷方式打开工具。"));
        dynamic locator = Activator.CreateInstance(locatorType)!;
        dynamic services = locator.ConnectServer(".", "root\\cimv2");
        dynamic processClass = services.Get("Win32_Process");
        dynamic parameters = processClass.Methods_.Item("Create").InParameters.SpawnInstance_();
        parameters.Properties_.Item("CommandLine").Value = commandLine;
        parameters.Properties_.Item("CurrentDirectory").Value = AppContext.BaseDirectory;
        dynamic startup = services.Get("Win32_ProcessStartup").SpawnInstance_();
        startup.Properties_.Item("ShowWindow").Value = arguments.Length == 0 ? 1 : 0;
        startup.Properties_.Item("WinstationDesktop").Value = "winsta0\\default";
        startup.Properties_.Item("CreateFlags").Value = 1024;
        startup.Properties_.Item("EnvironmentVariables").Value = Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
            .Select(entry => entry.Key + "=" + entry.Value).ToArray();
        parameters.Properties_.Item("ProcessStartupInformation").Value = startup;
        dynamic result = processClass.ExecMethod_("Create", parameters);
        uint code = Convert.ToUInt32(result.Properties_.Item("ReturnValue").Value);
        if (code != 0) throw new InvalidOperationException(Localization.Text("Windows 独立启动失败，状态码 ") + code);
        return Convert.ToInt32(result.Properties_.Item("ProcessId").Value);
    }

    private static string QuoteArgument(string value)
    {
        var quoted = new StringBuilder("\"");
        int slashes = 0;
        foreach (char character in value)
        {
            if (character == '\\')
            {
                slashes++;
                continue;
            }
            quoted.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            quoted.Append(character);
            slashes = 0;
        }
        quoted.Append('\\', slashes * 2);
        quoted.Append('"');
        return quoted.ToString();
    }
}
