using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PuzzleFactory;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PuzzleFactory.Editor
{
    internal static class SliceBuild
    {
        private const string ArtScene = "Assets/Factory/Workshop.unity";
        private const string GameScene = "Assets/Factory/FactoryGame.unity";

        [MenuItem("Puzzle Factory/Prepare and Test Three Shifts")]
        public static void Prepare()
        {
            Test();
            PrepareScene();
        }

        public static void Desktop()
        {
            Prepare();
            Build(BuildTarget.StandaloneWindows64, "Builds/Desktop/PuzzleFactory.exe");
        }

        public static void Android()
        {
            PrepareScene();
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.puzzlefactory3d.prototype");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.useCustomKeystore = false;
            EditorUserBuildSettings.buildAppBundle = false;
            Build(BuildTarget.Android, "Builds/Android/PuzzleFactory3D-v0.2.apk");
        }

        private static void PrepareScene()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                throw new InvalidOperationException("Save or close the current scene before preparing the slice.");

            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(GameScene) && !AssetDatabase.CopyAsset(ArtScene, GameScene))
                throw new IOException("Unable to create the gameplay scene from the approved Workshop scene.");
            var scene = EditorSceneManager.OpenScene(GameScene);
            if (!UnityEngine.Object.FindAnyObjectByType<FactoryGame>())
                new GameObject("Factory gameplay", typeof(FactoryGame));
            if (!EditorSceneManager.SaveScene(scene))
                throw new IOException("Unable to save the gameplay scene.");

            PlayerSettings.defaultScreenWidth = 540;
            PlayerSettings.defaultScreenHeight = 960;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.bundleVersion = "0.2.0";
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(GameScene, true) };
            AssetDatabase.SaveAssets();
        }

        private static void Build(BuildTarget target, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var summary = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { GameScene },
                target = target,
                locationPathName = path
            }).summary;
            string report = $"{target}: {summary.result}; {summary.totalErrors} errors; "
                            + $"{summary.totalWarnings} warnings; {summary.totalSize:N0} bytes.";
            Debug.Log(report);
            if (summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException(report);
        }

        public static void Test()
        {
            var checks = new List<string>();
            var definitions = ShiftDefinition.Load();
            for (int i = 0; i < definitions.Length; i++)
            {
                var level = definitions[i];
                var solved = Drive(level, (shift, _) => shift.Piece);
                Require(solved.Phase == ShiftPhase.Won && solved.Correct == level.pieces.Length && solved.Errors == 0,
                    "Perfect authored solution " + level.name);
                checks.Add($"PASS {level.name}: {solved.Correct}/{level.pieces.Length} sorted, "
                           + $"0 mistakes, {solved.Time:F2}s simulated.");
                var recovered = Drive(level, (shift, attempt) => attempt == 0 ? Wrong(shift) : shift.Piece);
                Require(recovered.Phase == ShiftPhase.Won && recovered.Errors == 1 &&
                        recovered.Correct == level.pieces.Length, "Recovery with replacement " + level.name);
                checks.Add($"PASS {level.name}: a misroute is requeued and all required pieces still arrive.");
                var failed = Drive(level, (shift, _) => Wrong(shift));
                Require(failed.Phase == ShiftPhase.Lost && failed.Errors == level.errorLimit,
                    "Loss threshold " + level.name);
                var retry = Drive(level, (shift, _) => shift.Piece);
                Require(retry.Phase == ShiftPhase.Won && retry.Errors == 0, "Retry " + level.name);
                checks.Add($"PASS {level.name}: {level.errorLimit} mistakes stops the line; fresh retry is solvable.");
            }

            var gate = new Shift(definitions[0]);
            gate.Advance(.75f);
            gate.Select(gate.Piece);
            gate.Advance(definitions[0].approach);
            Route committed = gate.Committed;
            gate.Cycle();
            Require(gate.Selected != committed && gate.Committed == committed, "Committed dispatch is immutable");
            Require(!gate.Select(Route.Yellow), "Unavailable route rejected");
            gate.Advance(definitions[0].dispatch);
            Require(gate.Correct == 1, "Post-commit touch affects only the next piece");
            checks.Add("PASS Junction input: allowed routes cycle; unavailable route is rejected;"
                       + " switching after commitment does not reroute a dispatched piece.");

            string key = "PuzzleFactory.AutomatedVerification." + Guid.NewGuid();
            try
            {
                var save = new ProgressFile(key);
                Require(save.IsUnlocked(0) && !save.IsUnlocked(1), "Initial level locks");
                Require(save.Complete(1) == 0 && !save.InstallArm(), "Locked stage and premature upgrade rejected");
                Require(save.Complete(0) == 50 && save.Complete(0) == 0, "Reward paid once");
                Require(save.InstallArm() && !save.InstallArm(), "Free upgrade applied once");
                var loaded = new ProgressFile(key);
                Require(loaded.Data.coins == 50 && loaded.Data.armInstalled && loaded.IsUnlocked(1) &&
                        !loaded.IsUnlocked(2), "Progress restored");
                Require(loaded.Complete(1) == 50 && loaded.Complete(2) == 50, "Sequential progress");
                var final = new ProgressFile(key);
                Require(final.Data.coins == 150 && final.Data.cleared == 3 && final.Data.armInstalled,
                    "All progress restored");
                checks.Add("PASS Save/reload: locked shifts, 50 coins once per clear, 150 total, "
                           + "and a one-time persistent visual upgrade.");
            }
            finally
            {
                PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
            }

            string destination = Path.GetFullPath(Path.Combine(Application.dataPath, "../../docs/playtest/tests.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.WriteAllLines(destination, checks);
            Debug.Log($"Slice acceptance: {checks.Count} passed checks.");
        }

        private static Shift Drive(ShiftDefinition definition, Func<Shift, int, Route> choose)
        {
            var shift = new Shift(definition);
            int attempt = 0;
            shift.Changed += item =>
            {
                if (item == ShiftEvent.Spawned)
                    shift.Select(choose(shift, attempt++));
            };
            shift.Advance(180);
            return shift;
        }

        private static Route Wrong(Shift shift) => shift.Routes.Single(route => route != shift.Piece);

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException("Acceptance check failed: " + message);
        }
    }
}
