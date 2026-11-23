using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PuzzleFactory
{
    internal sealed class ReviewCapture : MonoBehaviour
    {
        public static bool Active { get; private set; }

        public static bool Requested() => Application.platform == RuntimePlatform.WindowsPlayer &&
            Argument("-factoryCapture") != null;

        private static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int position = Array.IndexOf(args, name);
            return position >= 0 && position + 1 < args.Length ? args[position + 1] : null;
        }

        public void Run(FactoryGame game)
        {
            Active = true;
            StartCoroutine(Record(game, Path.GetFullPath(Argument("-factoryCapture"))));
        }

        private static IEnumerator Record(FactoryGame game, string output)
        {
            const int rate = 15;
            const float step = 2.5f / rate;
            const int width = 540;
            const int height = 960;
            string frames = Path.Combine(output, "frames");
            Directory.CreateDirectory(frames);
            var camera = Camera.main;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var resolved = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            camera.targetTexture = target;
            var seen = new HashSet<string>();
            bool intentionallyMissed = false;
            bool testedJunction = false;
            bool showFailure = false;
            FactoryGame.Page previous = game.ScreenPage;
            int hold = 0;
            try
            {
                for (int frame = 0; frame < rate * 90; frame++)
                {
                    string still = null;
                    switch (game.ScreenPage)
                    {
                        case FactoryGame.Page.Home:
                            if (hold == 12) still = "unity-home";
                            if (hold == 24) Press("START FIRST SHIFT");
                            break;
                        case FactoryGame.Page.Playing:
                            Shift shift = game.Current;
                            if (shift.Phase == ShiftPhase.Approaching)
                            {
                                if (shift.Fraction >= .33f && shift.Fraction < .52f)
                                    still = "unity-shift-" + (game.Level + 1);
                                if (shift.Fraction >= .43f)
                                {
                                    bool miss = showFailure || (game.Level == 0 && !intentionallyMissed && shift.Correct == 0 && shift.Errors == 0);
                                    Route route = miss ? shift.Routes.Single(r => r != shift.Piece) : shift.Piece;
                                    if (shift.Selected != route)
                                    {
                                        Press(testedJunction ? RouteButton(route) : "Tap junction");
                                        testedJunction = true;
                                        if (shift.Selected != route)
                                            throw new InvalidOperationException("Route control did not select " + route);
                                    }
                                }
                            }
                            if (shift.Errors > 0 && !showFailure)
                                intentionallyMissed = true;
                            game.Advance(step);
                            break;
                        case FactoryGame.Page.Result:
                            if (hold == 12)
                                still = showFailure ? "unity-loss" : game.Level == 0 ? "unity-win" : null;
                            if (hold >= 26)
                            {
                                if (showFailure)
                                {
                                    Press("RETRY SHIFT");
                                    if (game.ScreenPage != FactoryGame.Page.Playing || game.Current.Errors != 0)
                                        throw new InvalidOperationException("Retry button did not reset the shift.");
                                    File.WriteAllText(Path.Combine(output, "capture-complete.txt"),
                                        "Three wins, upgrade, junction and route controls, one loss, and retry completed through the player UI.");
                                    Application.Quit(0);
                                    yield break;
                                }
                                if (game.Level == 0)
                                    Press("INSTALL ROBOT ARM  /  FREE");
                                else if (game.Level == 1)
                                    Press("NEXT SHIFT");
                                else
                                {
                                    showFailure = true;
                                    Press("RETRY SHIFT");
                                }
                            }
                            break;
                        case FactoryGame.Page.Upgrade:
                            if (hold == 12) still = "unity-upgrade";
                            if (hold == 29) Press("NEXT SHIFT");
                            break;
                    }

                    if (game.ScreenPage != previous)
                    {
                        previous = game.ScreenPage;
                        hold = 0;
                    }
                    else
                        hold++;

                    yield return new WaitForEndOfFrame();
                    RenderTexture previousTarget = RenderTexture.active;
                    Canvas.ForceUpdateCanvases();
                    camera.Render();
                    Graphics.Blit(target, resolved);
                    RenderTexture.active = resolved;
                    image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    image.Apply(false);
                    RenderTexture.active = previousTarget;
                    if (frame == 12 && image.GetPixel(0, 0).maxColorComponent < .06f)
                    {
                        Debug.LogError("The capture camera did not render the workshop backdrop.");
                        Application.Quit(1);
                        yield break;
                    }
                    File.WriteAllBytes(Path.Combine(frames, $"frame-{frame:0000}.jpg"), image.EncodeToJPG(83));
                    if (still != null && seen.Add(still))
                        File.WriteAllBytes(Path.Combine(output, still + ".png"), image.EncodeToPNG());
                }
            }
            finally
            {
                camera.targetTexture = null;
                Destroy(image);
                Destroy(target);
                Destroy(resolved);
            }

            Debug.LogError("Factory review capture exhausted its frame budget.");
            Application.Quit(1);
        }

        private static string RouteButton(Route route)
        {
            switch (route)
            {
                case Route.Red: return "◇  RED";
                case Route.Blue: return "○  BLUE";
                default: return "△  AMBER";
            }
        }

        private static void Press(string label)
        {
            var control = GameObject.Find(label);
            if (!control || !control.TryGetComponent(out Button button) || !button.interactable)
                throw new InvalidOperationException("Missing enabled review control " + label);

            Canvas.ForceUpdateCanvases();
            var target = (RectTransform)control.transform;
            var pointer = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                position = RectTransformUtility.WorldToScreenPoint(Camera.main, target.TransformPoint(target.rect.center))
            };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            if (hits.Count == 0 || ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject) != control)
                throw new InvalidOperationException("The control is not reachable at its screen position: " + label);
            ExecuteEvents.Execute(control, pointer, ExecuteEvents.pointerClickHandler);
        }
    }
}
