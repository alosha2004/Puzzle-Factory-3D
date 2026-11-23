using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PuzzleFactory
{
    public enum Route { Red, Blue, Yellow }
    public enum ShiftPhase { Waiting, Approaching, Dispatching, Won, Lost }
    public enum ShiftEvent { Spawned, Routed, Correct, Mistake, Won, Lost }

    [Serializable]
    public sealed class ShiftDefinition
    {
        public string name;
        public string hint;
        public int[] pieces;
        public float interval;
        public float approach;
        public float dispatch;
        public int errorLimit;

        public Route[] Routes => pieces.Distinct().OrderBy(route => route).Select(route => (Route)route).ToArray();

        public static ShiftDefinition[] Load()
        {
            var catalog = JsonUtility.FromJson<Catalog>(Resources.Load<TextAsset>("FactoryLevels").text);
            if (catalog.levels == null || catalog.levels.Length != 3)
                throw new InvalidOperationException("The vertical slice needs exactly three shifts.");
            foreach (var level in catalog.levels)
                if (level.pieces == null || level.pieces.Length == 0 ||
                    level.pieces.Any(route => route < 0 || route > 2) ||
                    level.Routes.Length != 2 || !(level.approach > 0 && level.dispatch > 0) ||
                    !(level.interval > level.approach + level.dispatch) || level.errorLimit < 1)
                    throw new InvalidOperationException("Invalid shift " + level.name);
            return catalog.levels;
        }

        [Serializable]
        private sealed class Catalog { public ShiftDefinition[] levels; }
    }

    public sealed class Shift
    {
        private readonly Queue<Route> remaining;
        private float phaseElapsed;
        private float phaseDuration = .75f;

        public Shift(ShiftDefinition definition)
        {
            Definition = definition;
            Routes = definition.Routes;
            remaining = new Queue<Route>(definition.pieces.Select(route => (Route)route));
            Selected = Routes[0];
        }

        public ShiftDefinition Definition { get; }
        public Route[] Routes { get; }
        public Route Selected { get; private set; }
        public Route Piece { get; private set; }
        public Route Committed { get; private set; }
        public ShiftPhase Phase { get; private set; }
        public float Fraction => Mathf.Clamp01(phaseElapsed / phaseDuration);
        public float Time { get; private set; }
        public int Correct { get; private set; }
        public int Errors { get; private set; }
        public bool Finished => Phase == ShiftPhase.Won || Phase == ShiftPhase.Lost;
        public event Action<ShiftEvent> Changed;

        public bool Select(Route route)
        {
            if (Finished || Array.IndexOf(Routes, route) < 0 || Selected == route)
                return false;
            Selected = route;
            return true;
        }

        public void Cycle()
        {
            Select(Routes[(Array.IndexOf(Routes, Selected) + 1) % Routes.Length]);
        }

        public void Advance(float seconds)
        {
            while (seconds > 0 && !Finished)
            {
                float untilBoundary = phaseDuration - phaseElapsed;
                float slice = Mathf.Min(seconds, untilBoundary);
                Time += slice;
                seconds -= slice;
                phaseElapsed += slice;
                if (slice < untilBoundary)
                    return;
                phaseElapsed = 0;
                switch (Phase)
                {
                    case ShiftPhase.Waiting:
                        Piece = remaining.Dequeue();
                        Phase = ShiftPhase.Approaching;
                        phaseDuration = Definition.approach;
                        Changed?.Invoke(ShiftEvent.Spawned);
                        break;
                    case ShiftPhase.Approaching:
                        Committed = Selected;
                        Phase = ShiftPhase.Dispatching;
                        phaseDuration = Definition.dispatch;
                        Changed?.Invoke(ShiftEvent.Routed);
                        break;
                    case ShiftPhase.Dispatching:
                        Arrive();
                        break;
                }
            }
        }

        private void Arrive()
        {
            if (Piece == Committed)
            {
                Correct++;
                Changed?.Invoke(ShiftEvent.Correct);
            }
            else
            {
                Errors++;
                remaining.Enqueue(Piece);
                Changed?.Invoke(ShiftEvent.Mistake);
            }

            if (Errors >= Definition.errorLimit)
            {
                Phase = ShiftPhase.Lost;
                Changed?.Invoke(ShiftEvent.Lost);
            }
            else if (remaining.Count == 0)
            {
                Phase = ShiftPhase.Won;
                Changed?.Invoke(ShiftEvent.Won);
            }
            else
            {
                Phase = ShiftPhase.Waiting;
                phaseDuration = Definition.interval - Definition.approach - Definition.dispatch;
            }
        }
    }

    [Serializable]
    public sealed class FactoryProgress
    {
        public int cleared;
        public int coins;
        public bool armInstalled;
    }

    public sealed class ProgressFile
    {
        public const int Reward = 50;
        private readonly string key;

        public ProgressFile(string key = "PuzzleFactory.Progress.v1")
        {
            this.key = key;
            try
            {
                Data = JsonUtility.FromJson<FactoryProgress>(PlayerPrefs.GetString(key, "{}"));
            }
            catch (ArgumentException)
            {
                Data = null;
            }

            Data ??= new FactoryProgress();
            Data.cleared = Mathf.Clamp(Data.cleared, 0, 3);
            Data.coins = Mathf.Max(0, Data.coins);
            Data.armInstalled &= Data.cleared > 0;
        }

        public FactoryProgress Data { get; }
        public bool IsUnlocked(int index) => index >= 0 && index < 3 && index <= Data.cleared;

        public int Complete(int index)
        {
            if (index != Data.cleared || !IsUnlocked(index))
                return 0;
            Data.cleared++;
            Data.coins += Reward;
            Save();
            return Reward;
        }

        public bool InstallArm()
        {
            if (Data.armInstalled || Data.cleared == 0)
                return false;
            Data.armInstalled = true;
            Save();
            return true;
        }

        private void Save()
        {
            PlayerPrefs.SetString(key, JsonUtility.ToJson(Data));
            PlayerPrefs.Save();
        }
    }
}
