using System;

using Python.Runtime;

namespace RelativeIllumination.Optiland;

/// <summary>
/// The Python.NET engine, started once per process and left running.
///
/// <para>Python cannot be restarted inside a process once shut down, so this never shuts it
/// down: the interpreter lives as long as the program does. The lifecycle - setting
/// <c>Runtime.PythonDLL</c> and <c>PythonEngine.PythonHome</c> before
/// <c>PythonEngine.Initialize</c>, then releasing the GIL so other threads can take it - follows
/// <c>PythonEngineManager</c> in the OptilandNet repository.</para>
/// </summary>
public static class PythonSession
{
    private static readonly object Gate = new();
    private static bool _started;

    public static bool IsStarted { get { lock (Gate) return _started; } }

    /// <summary>Starts the interpreter if it is not already running.</summary>
    public static void Start()
    {
        lock (Gate)
        {
            if (_started) return;
            if (!PythonEnvironment.IsReady) throw new InvalidOperationException(PythonEnvironment.SetupHint);

            Runtime.PythonDLL = PythonEnvironment.PythonDll;
            PythonEngine.PythonHome = PythonEnvironment.Home;
            PythonEngine.Initialize();
            PythonEngine.BeginAllowThreads();
            _started = true;
        }
    }

    /// <summary>Runs <paramref name="func"/> holding the GIL.</summary>
    public static T WithGil<T>(Func<T> func)
    {
        Start();
        using (Py.GIL()) return func();
    }

    /// <summary>Runs <paramref name="action"/> holding the GIL.</summary>
    public static void WithGil(Action action)
    {
        Start();
        using (Py.GIL()) action();
    }

    /// <summary>The optiland version string, which also proves the import works.</summary>
    public static string OptilandVersion =>
        WithGil(() => Py.Import("optiland").GetAttr("__version__").ToString() ?? "unknown");
}
