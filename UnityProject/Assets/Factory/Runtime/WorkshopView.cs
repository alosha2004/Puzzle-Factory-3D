using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PuzzleFactory
{
    internal sealed class WorkshopView
    {
        private static readonly Color Coral = new Color32(255, 103, 104, 255);
        private static readonly Color Blue = new Color32(81, 209, 255, 255);
        private static readonly Color Gold = new Color32(255, 192, 78, 255);

        private readonly Transform root;
        private readonly MeshRenderer[] arm;
        private readonly MeshRenderer[][] tokens = new MeshRenderer[3][];
        private readonly GameObject token;
        private readonly LineRenderer gate;
        private readonly LineRenderer feedback;
        private readonly Material beltMaterial;
        private readonly Material signals;
        private readonly Vector3[] ring = new Vector3[33];
        private readonly AudioSource audio;
        private readonly AudioClip click;
        private readonly AudioClip good;
        private readonly AudioClip bad;
        private readonly AudioClip installed;
        private float flashUntil;
        private Route flashRoute;
        private bool flashSuccess;

        public WorkshopView(Transform parent)
        {
            root = parent;
            var imported = GameObject.Find("Factory workshop — Blender import");
            var renderers = imported.GetComponentsInChildren<MeshRenderer>();
            arm = renderers.Where(r => r.name.StartsWith("Upgrade arm display__", StringComparison.Ordinal)).ToArray();
            var finishes = new Dictionary<string, Material>();
            foreach (var renderer in renderers)
            {
                string[] designation = renderer.name.Split(new[] { "__" }, StringSplitOptions.None);
                if (designation.Length != 2)
                    continue;
                finishes[designation[1]] = renderer.sharedMaterial;
                if (designation[0] == "Puzzle pieces")
                    renderer.enabled = false;
            }

            token = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("GameplayTokens"), root);
            token.name = "Live Blender piece";
            for (int i = 0; i < tokens.Length; i++)
            {
                string name = ((Route)i).ToString();
                string finish = new[] { "Vermilion", "Cobalt", "Sunflower" }[i];
                tokens[i] = token.GetComponentsInChildren<MeshRenderer>()
                    .Where(r => r.name == name + "Body" || r.name == name + "Mark").ToArray();
                if (tokens[i].Length != 2)
                    throw new InvalidOperationException("Missing reusable Blender piece " + name);
                foreach (var renderer in tokens[i])
                    renderer.sharedMaterial = finishes[renderer.name.EndsWith("Body") ? finish : "Ceramic"];
            }
            token.SetActive(false);

            beltMaterial = new Material(Resources.Load<Shader>("BeltFlow"));
            Belt("Infeed moving tread", 0, -5.03f, 0, -1.68f, .84f, false);
            Belt("Center moving tread", 0, -.69f, 0, 3.65f, .75f, false);
            Belt("Red moving tread", -.72f, -1.04f, -3.15f, 3.65f, .70f, true);
            Belt("Amber moving tread", .72f, -1.04f, 3.15f, 3.65f, .70f, true);

            signals = new Material(Resources.Load<Shader>("Signal"));
            gate = Line("Selected route", .076f);
            feedback = Line("Sorter feedback ring", .035f);
            audio = parent.gameObject.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            click = Tone("Gate click", .085f, 650, 440);
            good = Tone("Correct sort", .2f, 510, 940);
            bad = Tone("Wrong route", .22f, 215, 115);
            installed = Tone("Arm installed", .42f, 390, 1120);
        }

        public static Vector3 Point(float x, float y, float altitude) => new Vector3(-x, altitude, -y);

        public void ShowArm(bool visible)
        {
            foreach (var renderer in arm)
                renderer.enabled = visible;
        }

        public void Select(Route route, bool audible)
        {
            float x = route == Route.Blue ? 0 : route == Route.Red ? -.76f : .76f;
            gate.enabled = true;
            gate.positionCount = 3;
            gate.SetPosition(0, Point(0, -1.52f, .636f));
            gate.SetPosition(1, Point(x * .55f, -1.22f, .636f));
            gate.SetPosition(2, Point(x, route == Route.Blue ? -.76f : -.93f, .636f));
            gate.sharedMaterial.color = ColorFor(route);
            if (audible)
                audio.PlayOneShot(click, .32f);
        }

        public void SetIdle()
        {
            token.SetActive(false);
            gate.enabled = false;
            feedback.enabled = false;
            flashUntil = 0;
            beltMaterial.SetFloat("_Travel", 0);
        }

        public void Tick(Shift shift)
        {
            beltMaterial.SetFloat("_Travel", shift.Time * (3.35f / shift.Definition.approach));
            bool visible = shift.Phase == ShiftPhase.Approaching || shift.Phase == ShiftPhase.Dispatching;
            token.SetActive(visible);
            if (visible)
            {
                for (int i = 0; i < tokens.Length; i++)
                    foreach (var renderer in tokens[i])
                        renderer.enabled = (int)shift.Piece == i;

                token.transform.position = shift.Phase == ShiftPhase.Approaching
                    ? Point(0, Mathf.Lerp(-4.95f, -1.50f, shift.Fraction), .576f)
                    : Exit(shift.Committed, shift.Fraction);
            }

            float remaining = flashUntil - shift.Time;
            feedback.enabled = remaining > 0;
            if (remaining > 0)
                Ring(flashRoute, 1 - remaining / .65f, flashSuccess);
        }

        public void Result(Shift shift, bool success)
        {
            flashSuccess = success;
            flashRoute = shift.Committed;
            flashUntil = shift.Time + .65f;
            audio.PlayOneShot(success ? good : bad, .38f);
        }

        public void Installed() => audio.PlayOneShot(installed, .36f);

        private static Vector3 Exit(Route route, float fraction)
        {
            float side = route == Route.Red ? -1 : route == Route.Yellow ? 1 : 0;
            if (fraction < .17f)
            {
                float step = fraction / .17f;
                return Point(Mathf.Lerp(0, side * .72f, step),
                    Mathf.Lerp(-1.50f, side == 0 ? -.69f : -1.04f, step), .576f);
            }

            float t = (fraction - .17f) / .83f;
            float blend = t * t * (3 - 2 * t);
            return Point(side * Mathf.Lerp(.72f, 3.15f, blend),
                Mathf.Lerp(side == 0 ? -.69f : -1.04f, 4.17f, t), .576f);
        }

        private static Color ColorFor(Route route) => route == Route.Red ? Coral : route == Route.Blue ? Blue : Gold;

        private void Belt(string label, float x0, float y0, float x1, float y1, float width, bool curved)
        {
            const int steps = 32;
            var vertices = new Vector3[(steps + 1) * 2];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[steps * 6];
            Vector2 previous = new Vector2(x0, y0);
            float length = 0;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float blend = curved ? t * t * (3 - 2 * t) : t;
                var mid = new Vector2(Mathf.Lerp(x0, x1, blend), Mathf.Lerp(y0, y1, t));
                var cross = new Vector2(y1 - y0, -(x1 - x0) * (curved ? 6 * t * (1 - t) : 1)).normalized;
                length += Vector2.Distance(previous, mid);
                previous = mid;
                int edge = i * 2;
                Vector2 left = mid - cross * (width * .5f);
                Vector2 right = mid + cross * (width * .5f);
                vertices[edge] = Point(left.x, left.y, .574f);
                vertices[edge + 1] = Point(right.x, right.y, .574f);
                uv[edge] = new Vector2(0, length);
                uv[edge + 1] = new Vector2(1, length);
                if (i == steps)
                    continue;
                int start = i * 6;
                triangles[start] = edge;
                triangles[start + 1] = edge + 2;
                triangles[start + 2] = edge + 1;
                triangles[start + 3] = edge + 1;
                triangles[start + 4] = edge + 2;
                triangles[start + 5] = edge + 3;
            }

            var mesh = new Mesh { name = label, vertices = vertices, uv = uv, triangles = triangles };
            mesh.RecalculateNormals();
            var obj = new GameObject(label, typeof(MeshFilter), typeof(MeshRenderer));
            obj.transform.SetParent(root, false);
            obj.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = obj.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = beltMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private LineRenderer Line(string label, float width)
        {
            var obj = new GameObject(label);
            obj.transform.SetParent(root, false);
            var line = obj.AddComponent<LineRenderer>();
            line.sharedMaterial = new Material(signals);
            line.widthMultiplier = width;
            line.numCapVertices = 4;
            line.enabled = false;
            return line;
        }

        private void Ring(Route route, float progress, bool success)
        {
            float x = route == Route.Red ? -3.15f : route == Route.Yellow ? 3.15f : 0;
            float radius = .22f + progress * .55f;
            for (int i = 0; i < ring.Length; i++)
            {
                float angle = i / (ring.Length - 1f) * Mathf.PI * 2;
                ring[i] = Point(x + Mathf.Cos(angle) * radius, 3.44f, .83f + Mathf.Sin(angle) * radius);
            }
            feedback.sharedMaterial.color = success ? new Color32(134, 255, 161, 255) : Coral;
            feedback.positionCount = ring.Length;
            feedback.SetPositions(ring);
        }

        private static AudioClip Tone(string name, float seconds, float from, float to)
        {
            const int rate = 22050;
            var samples = new float[Mathf.RoundToInt(rate * seconds)];
            float phase = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (samples.Length - 1f);
                phase += Mathf.Lerp(from, to, t) * Mathf.PI * 2 / rate;
                float envelope = Mathf.Min(t * 32, 1) * (1 - t) * (1 - t);
                samples[i] = (Mathf.Sin(phase) + .16f * Mathf.Sin(phase * 2)) * envelope * .52f;
            }
            var clip = AudioClip.Create(name, samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
