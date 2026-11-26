using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace BackgroundLoading
{
    /// <summary>
    /// Finds out who switches Application.runInBackground off. A Harmony prefix on the
    /// setter logs every "set to false" that BackgroundLoading did not make itself, with
    /// the calling mod (from the managed stack) and the game's IL2CPP stack.
    /// It only reports; the per-frame check in BackgroundLoadingMod still switches it back on.
    /// </summary>
    internal static class RunInBackgroundWatch
    {
        // Full reports logged per session; later ones from an already-reported caller are skipped.
        private const int MaxReports = 10;
        private const int MaxFramesShown = 25;

        // True while BackgroundLoading itself is calling the setter.
        private static bool s_ownSet;
        private static int s_reportCount;
        private static readonly HashSet<string> s_reportedCallers = new HashSet<string>();

        /// <summary>True once the setter patch has been applied.</summary>
        internal static bool Installed { get; private set; }

        /// <summary>
        /// Set by the prefix when something else set the value to false. The per-frame check
        /// reads and clears it, to say whether the patch saw that change.
        /// </summary>
        internal static bool SawForeignFalseSet;

        internal static void Install(HarmonyLib.Harmony harmony, MelonLogger.Instance log)
        {
            try
            {
                MethodInfo setter = AccessTools.PropertySetter(typeof(Application), nameof(Application.runInBackground));
                if (setter == null)
                {
                    log.Warning("Could not find the Application.runInBackground setter; callers that switch it off will not be named.");
                    return;
                }
                harmony.Patch(setter, prefix: new HarmonyMethod(typeof(RunInBackgroundWatch), nameof(SetterPrefix)));
                Installed = true;
            }
            catch (Exception ex)
            {
                log.Warning($"Could not patch the Application.runInBackground setter ({ex.Message}); callers that switch it off will not be named.");
            }
        }

        /// <summary>Use this for every set BackgroundLoading makes, so it is not reported.</summary>
        internal static void Set(bool value)
        {
            bool previous = s_ownSet;
            s_ownSet = true;
            try
            {
                Application.runInBackground = value;
            }
            finally
            {
                s_ownSet = previous;
            }
        }

        // Harmony matches "value" to the setter's parameter by name.
        private static void SetterPrefix(bool value)
        {
            if (value || s_ownSet)
                return;
            try
            {
                SawForeignFalseSet = true;
                Report();
            }
            catch
            {
                // Never let reporting break the caller.
            }
        }

        private static void Report()
        {
            string culprit;
            string managedTrace = BuildManagedTrace(out culprit);
            string il2cppTrace = GetIl2CppTrace();

            string key = culprit ?? FirstLine(il2cppTrace) ?? "unknown";
            if (s_reportedCallers.Contains(key) || s_reportCount >= MaxReports)
                return;
            s_reportedCallers.Add(key);
            s_reportCount++;

            MelonLogger.Instance log = BackgroundLoadingMod.Log;
            if (log == null)
                return;

            string state = BackgroundLoadingMod.IsActive ? "while BackgroundLoading is ON" : "while BackgroundLoading is OFF";
            var sb = new StringBuilder();
            if (culprit != null)
            {
                sb.Append($"[runInBackground culprit] Case 1 (managed mod code): {culprit} set Application.runInBackground = false {state}.");
            }
            else
            {
                sb.Append($"[runInBackground culprit] Case 2 (game/native code): Application.runInBackground was set to false {state}, with no mod method on the managed stack. ");
                sb.Append("The IL2CPP stack below shows the game code that did it.");
            }
            sb.AppendLine();
            sb.AppendLine("  Managed stack:");
            sb.Append(managedTrace.Length > 0 ? managedTrace : "    (no managed frames outside Harmony/MelonLoader/BackgroundLoading)" + Environment.NewLine);
            sb.AppendLine("  IL2CPP stack:");
            sb.Append(Indent(il2cppTrace ?? "(not available)"));
            log.Warning(sb.ToString().TrimEnd());
        }

        // Builds a readable managed stack and picks the first frame that belongs to another mod.
        private static string BuildManagedTrace(out string culprit)
        {
            culprit = null;
            var sb = new StringBuilder();
            StackFrame[] frames = new StackTrace(2, false).GetFrames();
            if (frames == null)
                return string.Empty;

            Assembly ours = typeof(RunInBackgroundWatch).Assembly;
            int shown = 0;
            foreach (StackFrame frame in frames)
            {
                MethodBase method = frame.GetMethod();
                if (method == null)
                    continue;
                Type type = method.DeclaringType;
                Assembly asm = type?.Assembly;
                string asmName = asm?.GetName().Name ?? "";

                if (asm == ours || IsInfrastructure(asmName))
                    continue;

                string where = $"{(type != null ? type.FullName : "<dynamic>")}.{method.Name}";
                string owner = asm != null ? DescribeAssembly(asm) : "Harmony patch wrapper";

                if (culprit == null && asm != null && !IsGameAssembly(asmName))
                    culprit = $"{owner}, method {where}";

                if (shown < MaxFramesShown)
                {
                    sb.AppendLine($"    at {where}  [{owner}]");
                    shown++;
                }
            }
            return sb.ToString();
        }

        private static bool IsInfrastructure(string asmName) =>
            asmName.StartsWith("0Harmony", StringComparison.Ordinal)
            || asmName.StartsWith("HarmonyLib", StringComparison.Ordinal)
            || asmName.StartsWith("MonoMod", StringComparison.Ordinal)
            || asmName.StartsWith("Il2CppInterop", StringComparison.Ordinal)
            || asmName.StartsWith("MelonLoader", StringComparison.Ordinal)
            || asmName.StartsWith("System", StringComparison.Ordinal)
            || asmName == "mscorlib"
            || asmName == "netstandard";

        // Interop wrappers for the game and Unity: they show up when a mod calls game code
        // that sets the value, but the mod further out is the one to name.
        private static bool IsGameAssembly(string asmName) =>
            asmName.StartsWith("Il2Cpp", StringComparison.Ordinal)
            || asmName.StartsWith("UnityEngine", StringComparison.Ordinal)
            || asmName.StartsWith("Unity.", StringComparison.Ordinal)
            || asmName.StartsWith("Assembly-CSharp", StringComparison.Ordinal);

        private static string DescribeAssembly(Assembly asm)
        {
            string asmName = asm.GetName().Name;
            try
            {
                foreach (MelonBase melon in MelonBase.RegisteredMelons)
                {
                    if (melon?.MelonAssembly?.Assembly == asm)
                        return $"mod \"{melon.Info.Name}\" v{melon.Info.Version} by {melon.Info.Author} ({asmName}.dll)";
                }
            }
            catch
            {
                // Fall back to the assembly name.
            }
            return $"{asmName}.dll";
        }

        private static string GetIl2CppTrace()
        {
            try
            {
                string trace = StackTraceUtility.ExtractStackTrace();
                if (!string.IsNullOrWhiteSpace(trace))
                    return trace.Trim();
            }
            catch
            {
                // Try the IL2CPP mscorlib one instead.
            }
            try
            {
                string trace = new Il2CppSystem.Diagnostics.StackTrace().ToString();
                if (!string.IsNullOrWhiteSpace(trace))
                    return trace.Trim();
            }
            catch
            {
                // Not available in this build.
            }
            return null;
        }

        private static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text))
                return null;
            int nl = text.IndexOf('\n');
            return (nl < 0 ? text : text.Substring(0, nl)).Trim();
        }

        private static string Indent(string text)
        {
            var sb = new StringBuilder();
            foreach (string line in text.Split('\n'))
                sb.AppendLine("    " + line.TrimEnd('\r'));
            return sb.ToString();
        }
    }
}
