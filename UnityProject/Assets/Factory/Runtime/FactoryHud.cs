using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PuzzleFactory
{
    internal sealed class FactoryHud
    {
        private static readonly Color Ink = new Color32(16, 28, 49, 248);
        private static readonly Color PanelBlue = new Color32(30, 51, 80, 251);
        private static readonly Color Slate = new Color32(46, 72, 103, 255);
        private static readonly Color Paper = new Color32(244, 249, 255, 255);
        private static readonly Color Mist = new Color32(175, 204, 229, 255);
        private static readonly Color Gold = new Color32(255, 189, 77, 255);
        private static readonly Color Cyan = new Color32(99, 222, 255, 255);
        private static readonly Color Green = new Color32(151, 241, 153, 255);
        private static readonly Color Coral = new Color32(255, 124, 125, 255);
        private static readonly Vector2 Top = new Vector2(.5f, 1);
        private static readonly Vector2 Middle = new Vector2(.5f, .5f);
        private static readonly Vector2 Bottom = new Vector2(.5f, 0);
        private static readonly string[] Symbols = { "◇", "○", "△" };

        private readonly RectTransform root;
        private readonly Font font;
        private readonly Sprite rounded;
        private RectTransform screen;
        private Text count;
        private Text clock;
        private Text errors;
        private Text message;
        private RectTransform gate;
        private Button[] routes;
        private Text[] routeLabels;
        private Rect safeViewport;

        public FactoryHud(Camera camera)
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            rounded = RoundedSprite();
            var obj = new GameObject("Factory interface", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = obj.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1;
            var scaler = obj.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(720, 1280);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .35f;
            root = new GameObject("Display safe area", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(obj.transform, false);
            FitSafeArea();
            if (!EventSystem.current)
                new GameObject("Touch input", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        public void FitSafeArea()
        {
            Rect area = Screen.safeArea;
            var next = new Rect(area.x / Screen.width, area.y / Screen.height,
                area.width / Screen.width, area.height / Screen.height);
            if (next.width <= 0 || next.height <= 0)
                next = new Rect(0, 0, 1, 1);
            if (next == safeViewport)
                return;
            safeViewport = next;
            root.anchorMin = next.min;
            root.anchorMax = next.max;
            root.offsetMin = root.offsetMax = Vector2.zero;
        }

        public void Home(FactoryProgress progress, Action start, Action levels)
        {
            Page("Workshop home");
            Card(screen, Top, 0, -124, 624, 170, Ink);
            Label(screen, Top, 0, -82, 594, 45, "PUZZLE FACTORY", 46, Paper, true);
            Label(screen, Top, 0, -144, 574, 30, "3D  /  SORTING WORKSHOP", 22, Cyan, true);
            Label(screen, Top, 0, -181, 574, 30, $"{progress.coins} COINS   ·   {progress.cleared}/3 SHIFTS", 19, Gold);
            Card(screen, Bottom, 0, 121, 624, 192, Ink);
            Button(screen, Bottom, 0, 153, 566, 68, progress.cleared == 0 ? "START FIRST SHIFT" :
                progress.cleared == 3 ? "REPLAY SHIFTS" : "CONTINUE", Gold, Ink, start);
            Button(screen, Bottom, 0, 75, 566, 56, "SELECT SHIFT", Slate, Paper, levels);
        }

        public void Levels(ShiftDefinition[] levels, ProgressFile progress, Action<int> start, Action back)
        {
            Page("Shift selection");
            Card(screen, Middle, 0, 0, 624, 580, Ink);
            Label(screen, Middle, 0, 238, 562, 66, "SELECT SHIFT", 44, Paper, true);
            for (int i = 0; i < levels.Length; i++)
            {
                int index = i;
                bool unlocked = progress.IsUnlocked(i);
                var button = Button(screen, Middle, 0, 132 - (i * 112), 542, 82,
                    $"0{i + 1}   {levels[i].name.ToUpperInvariant()}" + (unlocked ? "" : "   /   LOCKED"),
                    unlocked ? Slate : PanelBlue, unlocked ? Paper : Mist, () => start(index));
                button.interactable = unlocked;
            }
            Button(screen, Middle, 0, -225, 542, 58, "WORKSHOP", Gold, Ink, back);
        }

        public void Play(int level, Shift shift, Action pause, Action cycle, Action<Route> select)
        {
            Page("Live shift");
            Card(screen, Top, 0, -118, 640, 163, Ink);
            Label(screen, Top, -117, -68, 380, 33, $"SHIFT 0{level + 1}   /   {shift.Definition.name.ToUpperInvariant()}",
                21, Cyan, true, TextAnchor.MiddleLeft);
            Button(screen, Top, 253, -70, 93, 47, "II", Slate, Paper, pause);
            count = Label(screen, Top, -196, -121, 205, 43, "", 32, Paper, true);
            clock = Label(screen, Top, 6, -121, 174, 43, "", 28, Paper, true);
            errors = Label(screen, Top, 206, -121, 178, 43, "", 23, Gold, true);
            message = Label(screen, Top, 0, -171, 600, 38, "", 19, Mist);
            Card(screen, Bottom, 0, 122, 640, 186, Ink);
            Label(screen, Bottom, 0, 182, 610, 35, "ROUTE THE NEXT PIECE", 20, Mist, true);
            routes = new Button[2];
            routeLabels = new Text[2];
            for (int i = 0; i < shift.Routes.Length; i++)
            {
                Route route = shift.Routes[i];
                routes[i] = Button(screen, Bottom, i == 0 ? -151 : 151, 112, 282, 82,
                    $"{Symbols[(int)route]}  {RouteName(route)}", Slate, Paper, () => select(route));
                routeLabels[i] = routes[i].GetComponentInChildren<Text>();
            }
            Label(screen, Bottom, 0, 57, 610, 36, "Tap a route or tap the junction in the workshop", 16, Mist);
            gate = Place(screen, "Tap junction", Middle, 0, 0, 148, 98);
            var touch = gate.gameObject.AddComponent<Image>();
            touch.color = Color.clear;
            var gateButton = gate.gameObject.AddComponent<Button>();
            gateButton.transition = Selectable.Transition.None;
            gateButton.onClick.AddListener(() => cycle());
            gate.SetAsFirstSibling();
        }

        public void UpdatePlay(Shift shift, string feedback, Camera camera)
        {
            count.text = $"{shift.Correct}/{shift.Definition.pieces.Length} SORTED";
            clock.text = $"{Mathf.FloorToInt(shift.Time / 60):00}:{Mathf.FloorToInt(shift.Time % 60):00}";
            errors.text = $"{shift.Errors}/{shift.Definition.errorLimit} ERRORS";
            errors.color = shift.Errors > 0 ? Coral : Gold;
            message.text = feedback;
            for (int i = 0; i < routes.Length; i++)
            {
                bool active = shift.Selected == shift.Routes[i];
                routes[i].image.color = active ? RouteColor(shift.Routes[i]) : Slate;
                routeLabels[i].color = active ? Ink : Paper;
            }
            Vector3 p = camera.WorldToViewportPoint(WorkshopView.Point(0, -1.2f, .58f));
            gate.anchorMin = gate.anchorMax = new Vector2(
                (p.x - safeViewport.xMin) / safeViewport.width,
                (p.y - safeViewport.yMin) / safeViewport.height);
            gate.anchoredPosition = Vector2.zero;
        }

        public void Pause(bool muted, Action resume, Action sound, Action home)
        {
            Page("Paused");
            Card(screen, Middle, 0, 0, 574, 367, Ink);
            Label(screen, Middle, 0, 124, 522, 58, "SHIFT PAUSED", 40, Paper, true);
            Button(screen, Middle, 0, 41, 506, 65, "RESUME", Gold, Ink, resume);
            Button(screen, Middle, 0, -41, 506, 62, muted ? "SOUND OFF" : "SOUND ON", Slate, Paper, sound);
            Button(screen, Middle, 0, -121, 506, 62, "WORKSHOP", Slate, Paper, home);
        }

        public void Result(bool won, int reward, bool canUpgrade, bool hasNext,
                           Action elevate, Action next, Action retry, Action home)
        {
            Page(won ? "Shift complete" : "Shift stopped");
            Card(screen, Middle, 0, 0, 620, 478, Ink);
            Label(screen, Middle, 0, 174, 576, 65, won ? "SHIFT COMPLETE" : "LINE STOPPED",
                45, won ? Green : Coral, true);
            Label(screen, Middle, 0, 105, 576, 55, won ? (reward > 0 ? $"+{reward} COINS" : "SHIFT REPLAYED") :
                "Too many pieces reached the wrong sorter.", won ? 29 : 22, won ? Gold : Mist, true);
            Action primary = canUpgrade ? elevate : won && hasNext ? next : retry;
            string primaryText = canUpgrade ? "INSTALL ROBOT ARM  /  FREE" :
                won && hasNext ? "NEXT SHIFT" : "RETRY SHIFT";
            Button(screen, Middle, 0, 21, 544, 72, primaryText, Gold, Ink, primary);
            if (canUpgrade && hasNext)
                Button(screen, Middle, 0, -68, 544, 60, "NEXT SHIFT", Slate, Paper, next);
            else if (won && hasNext)
                Button(screen, Middle, 0, -68, 544, 60, "REPLAY SHIFT", Slate, Paper, retry);
            Button(screen, Middle, 0, -155, 544, 62, "WORKSHOP", Slate, Paper, home);
        }

        public void Upgrade(Action next, Action home)
        {
            Page("Workshop upgrade");
            Card(screen, Top, 0, -108, 624, 146, Ink);
            Label(screen, Top, 0, -81, 590, 54, "ARM INSTALLED", 40, Gold, true);
            Label(screen, Top, 0, -137, 590, 36, "Your workshop has a new robotic gripper.", 20, Mist);
            Card(screen, Bottom, 0, 111, 624, 169, Ink);
            Button(screen, Bottom, 0, 140, 560, 66, "NEXT SHIFT", Gold, Ink, next);
            Button(screen, Bottom, 0, 68, 560, 52, "WORKSHOP", Slate, Paper, home);
        }

        private void Page(string name)
        {
            if (screen)
            {
                screen.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(screen.gameObject);
            }
            screen = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            screen.SetParent(root, false);
            screen.anchorMin = Vector2.zero;
            screen.anchorMax = Vector2.one;
            screen.offsetMin = screen.offsetMax = Vector2.zero;
        }

        private void Card(RectTransform parent, Vector2 anchor, float x, float y, float w, float h, Color color)
        {
            Image image = Place(parent, "Panel", anchor, x, y, w, h).gameObject.AddComponent<Image>();
            image.sprite = rounded;
            image.type = Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = false;
        }

        private Button Button(RectTransform parent, Vector2 anchor, float x, float y, float w, float h,
                              string text, Color color, Color ink, Action click)
        {
            var position = Place(parent, text, anchor, x, y, w, h);
            var image = position.gameObject.AddComponent<Image>();
            image.sprite = rounded;
            image.type = Image.Type.Sliced;
            image.color = color;
            var button = position.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = new Color(.92f, .95f, 1);
            colors.pressedColor = new Color(.76f, .82f, .91f);
            colors.selectedColor = Color.white;
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => click());
            Label(position, Middle, 0, 0, w - 16, h - 8, text, 26, ink, true);
            return button;
        }

        private Text Label(RectTransform parent, Vector2 anchor, float x, float y, float w, float h,
                           string text, int size, Color color, bool bold = false,
                           TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            var label = Place(parent, "Label", anchor, x, y, w, h).gameObject.AddComponent<Text>();
            label.font = font;
            label.text = text;
            label.fontSize = size;
            label.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            label.color = color;
            label.alignment = alignment;
            label.raycastTarget = false;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = Mathf.Min(14, size);
            label.resizeTextMaxSize = size;
            return label;
        }

        private static RectTransform Place(RectTransform parent, string name, Vector2 anchor,
                                           float x, float y, float width, float height)
        {
            var result = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            result.SetParent(parent, false);
            result.anchorMin = result.anchorMax = anchor;
            result.anchoredPosition = new Vector2(x, y);
            result.sizeDelta = new Vector2(width, height);
            return result;
        }

        private static Sprite RoundedSprite()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(0, Mathf.Abs(x + .5f - size / 2f) - 16);
                    float dy = Mathf.Max(0, Mathf.Abs(y + .5f - size / 2f) - 16);
                    pixels[y * size + x] = new Color(1, 1, 1, Mathf.Clamp01(15.6f - Mathf.Sqrt(dx * dx + dy * dy)));
                }
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), Middle,
                100, 0, SpriteMeshType.FullRect, new Vector4(17, 17, 17, 17));
        }

        private static string RouteName(Route route) => route == Route.Yellow ? "AMBER" : route.ToString().ToUpperInvariant();
        private static Color RouteColor(Route route) => route == Route.Red ? Coral : route == Route.Blue ? Cyan : Gold;
    }
}
