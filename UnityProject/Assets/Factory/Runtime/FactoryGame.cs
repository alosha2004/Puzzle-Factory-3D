using System;
using UnityEngine;

namespace PuzzleFactory
{
    public sealed class FactoryGame : MonoBehaviour
    {
        private Camera cameraView;
        private WorkshopView workshop;
        private FactoryHud hud;
        private ProgressFile progress;
        private ShiftDefinition[] definitions;
        private string feedback;
        private float showFeedbackUntil;
        private bool muted;

        internal enum Page { Home, Levels, Playing, Paused, Result, Upgrade }
        internal Page ScreenPage { get; private set; }
        internal Shift Current { get; private set; }
        internal int Level { get; private set; }

        private void Awake()
        {
            Application.targetFrameRate = 60;
            Screen.orientation = ScreenOrientation.Portrait;
            bool record = ReviewCapture.Requested();
            Application.runInBackground = record;
            const string captureKey = "PuzzleFactory.ReviewCapture";
            if (record)
                PlayerPrefs.DeleteKey(captureKey);
            progress = record ? new ProgressFile(captureKey) : new ProgressFile();
            definitions = ShiftDefinition.Load();
            cameraView = Camera.main;
            if (!cameraView.GetComponent<AudioListener>())
                cameraView.gameObject.AddComponent<AudioListener>();
            workshop = new WorkshopView(transform);
            workshop.ShowArm(progress.Data.armInstalled);
            hud = new FactoryHud(cameraView);
            ShowHome();
            if (record)
                gameObject.AddComponent<ReviewCapture>().Run(this);
        }

        private void Update()
        {
            hud.FitSafeArea();
            cameraView.orthographicSize = 9.35f * Mathf.Max(1, (.5625f / cameraView.aspect));
            if (!ReviewCapture.Active)
                Advance(Mathf.Min(Time.deltaTime, .1f));
        }

        internal void Advance(float dt)
        {
            if (ScreenPage != Page.Playing)
                return;
            Current.Advance(dt);
            workshop.Tick(Current);
            if (ScreenPage == Page.Playing)
                hud.UpdatePlay(Current, Current.Time <= showFeedbackUntil ? feedback : Current.Definition.hint, cameraView);
        }

        internal void StartShift(int index)
        {
            if (!progress.IsUnlocked(index))
                return;
            Level = index;
            Current = new Shift(definitions[index]);
            Current.Changed += OnShiftEvent;
            workshop.SetIdle();
            ShowPlaying();
        }

        internal void Select(Route route)
        {
            if (ScreenPage == Page.Playing && Current.Select(route))
                workshop.Select(route, true);
        }

        private void Cycle()
        {
            Route route = Current.Selected;
            Current.Cycle();
            if (route != Current.Selected)
                workshop.Select(Current.Selected, true);
        }

        internal void ShowHome()
        {
            ScreenPage = Page.Home;
            workshop.SetIdle();
            hud.Home(progress.Data, () =>
            {
                if (progress.Data.cleared < definitions.Length)
                    StartShift(progress.Data.cleared);
                else
                    ShowLevels();
            }, ShowLevels);
        }

        private void ShowLevels()
        {
            ScreenPage = Page.Levels;
            hud.Levels(definitions, progress, StartShift, ShowHome);
        }

        private void ShowPlaying()
        {
            ScreenPage = Page.Playing;
            feedback = Current.Definition.hint;
            showFeedbackUntil = -1;
            workshop.Select(Current.Selected, false);
            hud.Play(Level, Current, ShowPause, Cycle, Select);
            hud.UpdatePlay(Current, feedback, cameraView);
        }

        private void ShowPause()
        {
            ScreenPage = Page.Paused;
            hud.Pause(muted, ShowPlaying, () =>
            {
                muted = !muted;
                AudioListener.volume = muted ? 0 : 1;
                ShowPause();
            }, ShowHome);
        }

        private void OnShiftEvent(ShiftEvent transition)
        {
            switch (transition)
            {
                case ShiftEvent.Correct:
                case ShiftEvent.Mistake:
                    bool success = transition == ShiftEvent.Correct;
                    workshop.Result(Current, success);
                    feedback = success ? "MATCHED   /   PIECE ACCEPTED" : "WRONG SORTER   /   PIECE REQUEUED";
                    showFeedbackUntil = Current.Time + 1.25f;
                    break;
                case ShiftEvent.Won:
                case ShiftEvent.Lost:
                    Finish(transition == ShiftEvent.Won);
                    break;
            }
        }

        private void Finish(bool won)
        {
            ScreenPage = Page.Result;
            int reward = won ? progress.Complete(Level) : 0;
            bool next = Level + 1 < definitions.Length;
            hud.Result(won, reward, won && progress.Data.cleared > 0 && !progress.Data.armInstalled, next,
                Upgrade, Next, () => StartShift(Level), ShowHome);
        }

        internal void Upgrade()
        {
            if (!progress.InstallArm())
                return;
            workshop.ShowArm(true);
            workshop.Installed();
            ScreenPage = Page.Upgrade;
            hud.Upgrade(Next, ShowHome);
        }

        private void Next()
        {
            if (progress.IsUnlocked(Level + 1))
                StartShift(Level + 1);
            else
                ShowHome();
        }
    }
}
