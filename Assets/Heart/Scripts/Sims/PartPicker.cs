using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using MedAtlas.Heart;

namespace MedAtlas.Sims
{
    /// <summary>Tap (or click) a part of the 3D scene to read about it: the model's own parts list (name, Latin name,
    /// description, function) is shown in a small card and the part is outlined. A short tap counts; a drag still orbits.
    /// A tap on empty space closes the card.</summary>
    public class PartPicker : MonoBehaviour
    {
        Camera cam; Transform canvas;
        readonly List<Dictionary<string, object>> parts = new List<Dictionary<string, object>>();
        RectTransform card; Text title, latin, body;
        Transform halo; Material haloMat; Renderer picked;
        Vector2 downPos; float downTime; bool armed;

        // Z-Anatomy / scene object words -> part ids of the web models (first id that the model has wins)
        static readonly (string key, string ids)[] Aliases =
        {
            ("left ventricle", "lv|heart"), ("right ventricle", "rv|heart"), ("left atrium", "la|heart"), ("right atrium", "ra|heart"),
            ("aorta", "aorta|artery|vessels|vessel|blood"), ("vena cava", "cava|vessels|vein|vessel|blood"),
            ("left lung", "left-lung|lungs|lung"), ("right lung", "right-lung|lungs|lung"), ("lobe of", "lungs|lung|right-lung"),
            ("trachea", "trachea|airway|lungs"), ("liver", "liver|lobule"), ("gallbladder", "bile|liver"), ("pancreas", "pancreas|beta"),
            ("spleen", "spleen"), ("stomach", "stomach|gut"), ("oesophagus", "oesophagus|gut"),
            ("duodenum", "intestine|gut"), ("jejunum", "intestine|gut"), ("colon", "intestine|gut|rectum"), ("appendix", "intestine|gut"),
            ("anal", "rectum|intestine"), ("pharynx", "oesophagus"), ("tongue", "tongue|bud|epi"), ("palate", "oesophagus"), ("epiglottis", "oesophagus|airway"),
            ("kidney", "kidney|kidneys"), ("renal", "kidney|kidneys|ureter"), ("ureter", "ureter|kidneys"), ("bladder", "bladder"), ("urethra", "bladder|duct"),
            ("suprarenal", "adrenal"), ("thyroid", "thyroid"), ("parathyroid", "parathyroid"), ("hypophysis", "pituitary|hypothalamus"),
            ("thalamus", "thalamus|scn|vlpo|arousal"), ("cerebellum", "cerebellum"), ("medulla", "medulla|centre|nts"), ("pons", "medulla|arousal"),
            ("midbrain", "snc|arousal"), ("corpus callosum", "cortex|brain"), ("testis", "testis"), ("epididymis", "duct"), ("ductus deferens", "duct"),
            ("ejaculatory", "duct"), ("prostate", "prostate"), ("seminal", "prostate"), ("penis", "testis"), ("glans", "testis"),
            ("uterus", "uterus|endometrium"), ("cervix", "cervix|uterus"), ("ovary", "ovary|follicle"), ("uterine tube", "tube"),
            ("femur", "bone"), ("tibia", "bone"), ("fibula", "bone"), ("patella", "tendon|bone"),
            ("rectus femoris", "quad|muscle"), ("vastus", "quad|muscle"), ("biceps femoris", "ham|muscle"), ("gastrocnemius", "muscle|pool"), ("soleus", "muscle|pool"),
            ("femoral nerve", "ia|nerve"), ("sciatic", "nerve|motor"), ("saphenous", "vein|pool"), ("popliteal vein", "vein|valve"), ("femoral vein", "vein|valve"),
            ("cornea", "cornea"), ("sclera", "cornea"), ("iris", "iris"), ("lens", "lens"), ("retina", "retina|fovea"), ("vitreous", "lens"), ("optic nerve", "retina"),
            ("rectus muscle", "eyes|muscle"), ("oblique muscle", "eyes|muscle"), ("levator", "eyes"),
            ("tympanic", "drum"), ("malleus", "ossicle"), ("incus", "ossicle"), ("stapes", "ossicle|oval"), ("cochlea", "bm|ihc|ohc|wave"),
            ("vestibule", "canals|macula|cupula"), ("vestibular nerve", "nerve"), ("cochlear nerve", "nerve"),
            ("olfactory", "bulb|orn|crib"), ("nasal", "epi|orn|cilia"), ("concha", "epi"),
            ("sympathetic", "chain"), ("vagus", "vagus"), ("spinal cord", "cord|horn"),
        };

        // class of an organ part (OrganView) -> part ids
        static readonly (string cls, string ids)[] ClassIds =
        {
            ("heart", "heart|lv"), ("lung", "lungs|lung|right-lung|left-lung"), ("artery", "aorta|artery|vessels|vessel"), ("vein", "vein|vessels|cava|vessel"),
            ("kidney", "kidney|kidneys"), ("cortex", "cortex|brain|prefrontal"), ("cerebellum", "cerebellum"), ("brainstem", "medulla|arousal"), ("deep", "thalamus|striatum"),
            ("gland", "pituitary|adrenal|prostate"), ("bone", "bone"), ("muscle", "muscle|quad"), ("muscle2", "muscle|ham"), ("nerve", "nerve"),
            ("intestine", "intestine|gut"), ("cord", "cord"), ("airway", "trachea|airway"),
        };

        public static PartPicker Attach(GameObject host, Camera cam, Transform canvas, List<object> partsList)
        {
            var p = host.AddComponent<PartPicker>(); p.cam = cam; p.canvas = canvas;
            if (partsList != null) foreach (var o in partsList) { var d = MiniJson.O(o); if (d != null) p.parts.Add(d); }
            p.BuildCard();
            return p;
        }

        void BuildCard()
        {
            card = UIKit.Rect("PartCard", canvas, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -120), new Vector2(720, 200));
            UIKit.PanelImage(card, UIKit.Card);
            var line = UIKit.Rect("Accent", card, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(0, 0), new Vector2(5, 0));
            UIKit.PanelImage(line, UIKit.Accent, false);
            title = UIKit.LabelAt(card, "", 26, UIKit.TextMain, new Vector2(24, -16), new Vector2(672, 34), UIKit.SemiBold);
            latin = UIKit.LabelAt(card, "", 17, UIKit.Accent, new Vector2(24, -52), new Vector2(672, 24), UIKit.Italic);
            body = UIKit.LabelAt(card, "", 17, UIKit.TextMain, new Vector2(24, -82), new Vector2(672, 100));
            card.gameObject.SetActive(false);
            haloMat = Opacity.Glass(new Color(1f, 0.95f, 0.6f), 0.05f, 0.45f);
            var h = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(h.GetComponent<Collider>());
            h.name = "PickHalo"; h.GetComponent<Renderer>().sharedMaterial = haloMat; halo = h.transform; h.SetActive(false);
        }

        static bool OverUI(Vector2 screen)
        {
            var es = EventSystem.current; if (es == null) return false;
            var results = new List<RaycastResult>();
            es.RaycastAll(new PointerEventData(es) { position = screen }, results);
            return results.Exists(r => r.module is GraphicRaycaster);
        }

        void Update()
        {
            var ptr = Pointer.current;
            if (ptr != null)
            {
                var pos = ptr.position.ReadValue();
                if (ptr.press.wasPressedThisFrame) { downPos = pos; downTime = Time.unscaledTime; armed = !OverUI(pos); }
                if (ptr.press.wasReleasedThisFrame && armed)
                {
                    armed = false;
                    float slop = Mathf.Max(10f, Screen.dpi > 0 ? Screen.dpi * 0.08f : 12f);   // a short tap, not an orbit drag
                    if ((pos - downPos).magnitude < slop && Time.unscaledTime - downTime < 0.45f) Tap(pos);
                }
            }
            if (halo.gameObject.activeSelf)
            {
                if (picked == null || !picked.gameObject.activeInHierarchy) { Hide(); return; }
                var b = picked.bounds; halo.position = b.center;
                halo.localScale = b.size * (1.08f + 0.04f * Mathf.Sin(Time.unscaledTime * 5f)) + Vector3.one * 0.04f;
            }
        }

        // Picking ray-casts the meshes themselves (bounds first, then the triangles): the WebGL build strips the engine's
        // primitive colliders, so physics ray casts find nothing there.
        readonly Dictionary<Mesh, (Vector3[] v, int[] t)> meshCache = new Dictionary<Mesh, (Vector3[], int[])>();

        static bool RayTri(Ray r, Vector3 a, Vector3 b, Vector3 c, out float t)
        {
            t = 0; var e1 = b - a; var e2 = c - a; var p = Vector3.Cross(r.direction, e2); float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-9f) return false;
            float inv = 1f / det; var s = r.origin - a; float u = Vector3.Dot(s, p) * inv; if (u < 0 || u > 1) return false;
            var q = Vector3.Cross(s, e1); float v = Vector3.Dot(r.direction, q) * inv; if (v < 0 || u + v > 1) return false;
            t = Vector3.Dot(e2, q) * inv; return t > 0;
        }

        bool HitMesh(Renderer r, Ray world, out float dist)
        {
            dist = float.MaxValue;
            Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null || !mesh.isReadable) return r.bounds.IntersectRay(world, out dist);   // built-in primitives may not be readable in a build: their box is close enough
            if (!meshCache.TryGetValue(mesh, out var md)) { md = (mesh.vertices, mesh.triangles); meshCache[mesh] = md; }
            var tr = r.transform;
            var local = new Ray(tr.InverseTransformPoint(world.origin), tr.InverseTransformDirection(world.direction));
            bool hit = false; float best = float.MaxValue;
            for (int i = 0; i + 2 < md.t.Length; i += 3)
                if (RayTri(local, md.v[md.t[i]], md.v[md.t[i + 1]], md.v[md.t[i + 2]], out float tl) && tl < best) { best = tl; hit = true; }
            if (hit) dist = Vector3.Distance(world.origin, tr.TransformPoint(local.origin + local.direction * best));
            return hit;
        }

        void Tap(Vector2 screen)
        {
            var ray = cam.ScreenPointToRay(screen);
            var hits = new List<(Renderer r, float d)>();
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                if (r.gameObject.layer == 2) continue;   // Ignore Raycast: labels, particles and lines of the web scene
                if (r.transform == halo || !r.enabled || !r.gameObject.activeInHierarchy || r.GetComponentInParent<Canvas>() != null) continue;
                if (!r.bounds.IntersectRay(ray)) continue;
                if (HitMesh(r, ray, out float d)) hits.Add((r, d));
            }
            hits.Sort((a, b) => a.d.CompareTo(b.d));
            Renderer best = null; Renderer smallestGlass = null; float glassVol = float.MaxValue;
            foreach (var (r, _) in hits)
            {
                bool glass = r.sharedMaterial != null && r.sharedMaterial.renderQueue >= 3000;
                if (!glass) { best = r; break; }   // the first solid thing behind any glass is what was meant
                var s = r.bounds.size; float v = s.x * s.y * s.z;
                if (v < glassVol) { glassVol = v; smallestGlass = r; }
            }
            if (best == null) best = smallestGlass;
            if (best == null) { Hide(); return; }
            Show(best);
        }

        void Show(Renderer r)
        {
            string name = r.gameObject.name, cls = null;
            var ov = r.GetComponentInParent<OrganView>();
            if (ov != null) { var pt = ov.Parts.Find(x => x.Name == name); if (pt != null) cls = pt.Cls; }
            var part = Find(name, cls);
            string t, l, d;
            if (part != null)
            {
                t = MiniJson.S(part, "name"); l = MiniJson.S(part, "latin");
                string desc = MiniJson.S(part, "description"), fn = MiniJson.S(part, "fn");
                d = desc + (string.IsNullOrEmpty(fn) ? "" : "\n<b>Функция:</b> " + fn);
            }
            else
            {
                // no matching entry in this model: still name what was tapped (anatomical name of the mesh)
                t = name.Contains("|") ? name.Split('|')[0] : name.Replace(".l", " (ляво)").Replace(".r", " (дясно)");
                l = ""; d = "Тази структура е част от 3D модела; моделът няма отделно описание за нея.";
            }
            title.text = t; latin.text = l; body.supportRichText = true; body.text = d;
            // portrait phones scale the 1920-wide canvas down a lot: bigger type, and the card sits below the title
            bool portrait = Screen.height > Screen.width;
            title.fontSize = portrait ? 38 : 26; latin.fontSize = portrait ? 26 : 17; body.fontSize = portrait ? 27 : 17;
            card.anchoredPosition = new Vector2(0, portrait ? -210 : -120);
            // size the card to its text and keep it inside the screen width
            var crt = (RectTransform)canvas;
            float w = Mathf.Min(720f, crt.rect.width - 32f);
            float th = title.fontSize * 1.35f, lh = string.IsNullOrEmpty(l) ? 0 : latin.fontSize * 1.4f;
            title.rectTransform.sizeDelta = new Vector2(w - 48, th); latin.rectTransform.sizeDelta = new Vector2(w - 48, lh);
            latin.rectTransform.anchoredPosition = new Vector2(24, -16 - th);
            body.rectTransform.sizeDelta = new Vector2(w - 48, 100);
            float bh = body.preferredHeight; body.rectTransform.sizeDelta = new Vector2(w - 48, bh);
            body.rectTransform.anchoredPosition = new Vector2(24, -22 - th - lh);
            card.sizeDelta = new Vector2(w, 22 + th + lh + bh + 22);
            card.SetAsLastSibling(); card.gameObject.SetActive(true);
            picked = r; halo.gameObject.SetActive(true);
        }

        public void Hide() { if (card) card.gameObject.SetActive(false); if (halo) halo.gameObject.SetActive(false); picked = null; }

        Dictionary<string, object> ById(string id)
        {
            foreach (var p in parts) if (string.Equals(MiniJson.S(p, "id"), id, System.StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        Dictionary<string, object> FirstOf(string ids)
        {
            foreach (var id in ids.Split('|')) { var p = ById(id.Trim()); if (p != null) return p; }
            return null;
        }

        Dictionary<string, object> Find(string objName, string cls)
        {
            if (parts.Count == 0) return null;
            if (objName.Contains("|") || ById(objName) != null) { var p = FirstOf(objName); if (p != null) return p; }   // tagged cell-scene objects
            string low = objName.ToLowerInvariant();
            foreach (var (key, ids) in Aliases) if (low.Contains(key)) { var p = FirstOf(ids); if (p != null) return p; }
            if (cls != null) foreach (var (c, ids) in ClassIds) if (c == cls) { var p = FirstOf(ids); if (p != null) return p; }
            foreach (var p in parts) { var id = MiniJson.S(p, "id"); if (id != null && id.Length >= 3 && low.Contains(id.ToLowerInvariant())) return p; }
            return null;
        }
    }
}

