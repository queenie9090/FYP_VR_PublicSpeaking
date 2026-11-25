using UnityEngine;
using UnityEngine.Rendering;

// Local visual feedback only. Each emitter reads its owner's validated emotion.
[DisallowMultipleComponent]
public sealed class NPCEmotionParticles : MonoBehaviour
{
    private enum Tile { Star, Drop, Spark, Smoke, Tremor, Bubble, Dust }
    private NPCEmotionController owner;
    private Transform head, leftEye, rightEye, leftArm, rightArm;
    private ParticleSystem marks, smoke;
    private System.Random random;
    private string shown;
    private float nextMark, nextSmoke;
    private int sequence;

    private void Start()
    {
        owner = GetComponent<NPCEmotionController>();
        var material = Resources.Load<Material>("NPCEmotionParticles");
        if (!owner || !material) { enabled = false; return; }
        var rig = owner.GetComponentInParent<Animator>() ?? owner.GetComponentInChildren<Animator>();
        if (rig && rig.isHuman)
        {
            head = rig.GetBoneTransform(HumanBodyBones.Head);
            leftEye = rig.GetBoneTransform(HumanBodyBones.LeftEye);
            rightEye = rig.GetBoneTransform(HumanBodyBones.RightEye);
            leftArm = rig.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            rightArm = rig.GetBoneTransform(HumanBodyBones.RightUpperArm);
        }
        random = new System.Random(GetInstanceID());
        marks = CreateEmitter("Emotion particles", material, 28);
        smoke = CreateEmitter("Angry smoke", material, 6);
        SetTile(smoke, Tile.Smoke);
        var growth = smoke.sizeOverLifetime;
        growth.enabled = true;
        growth.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0,.58f,1,1.45f));
        var drift = smoke.noise;
        drift.enabled = true;
        drift.strength = .025f;
        drift.frequency = .8f;
        drift.scrollSpeed = .25f;
        drift.quality = ParticleSystemNoiseQuality.Low;
    }

    private ParticleSystem CreateEmitter(string label, Material material, int limit)
    {
        var go = new GameObject(label);
        go.SetActive(false);
        go.transform.SetParent(transform, false);
        go.layer = owner.HeadMesh.gameObject.layer;
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        main.maxParticles = limit;
        main.startSpeed = 0;
        var emission = ps.emission;
        emission.enabled = false;
        var shape = ps.shape;
        shape.enabled = false;
        var sheet = ps.textureSheetAnimation;
        sheet.enabled = true;
        sheet.numTilesX = 8;
        sheet.numTilesY = 1;
        sheet.frameOverTime = 0;
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] {new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1)},
            new[] {new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .12f), new GradientAlphaKey(.85f, .65f), new GradientAlphaKey(0, 1)});
        fade.color = gradient;
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        go.SetActive(true);
        return ps;
    }

    private static void SetTile(ParticleSystem ps, Tile tile)
    {
        var sheet = ps.textureSheetAnimation;
        sheet.startFrame = ((int)tile + .05f) / 8f;
    }

    private void OnDisable() => Clear();

    private void Clear()
    {
        if (marks) marks.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (smoke) smoke.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        shown = null;
    }

    private void LateUpdate()
    {
        if (!marks) return;
        if (!owner || !owner.isActiveAndEnabled || !owner.HeadMesh ||
            !owner.HeadMesh.enabled || !owner.HeadMesh.gameObject.activeInHierarchy)
        {
            if (shown != null) Clear();
            return;
        }
        float strength = Mathf.Clamp01(owner.emotionIntensity * .01f);
        string emotion = owner.currentEmotion;
        if (emotion != "NEUTRAL" && strength <= 0)
        {
            if (shown != null) Clear();
            return;
        }
        if (shown != emotion)
        {
            Clear();
            shown = emotion;
            Tile tile = emotion switch {
                "HAPPY" => Tile.Star, "SAD" => Tile.Drop, "ANGRY" => Tile.Spark,
                "NERVOUS" => Tile.Tremor, "THINKING" => Tile.Bubble, _ => Tile.Dust
            };
            SetTile(marks, tile);
            marks.Play(false);
            sequence = 0;
            nextMark = nextSmoke = 0;
        }
        bool smoky = emotion == "ANGRY" && strength >= .55f;
        if (!smoky && smoke.isPlaying) smoke.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (Time.time < nextMark && (!smoky || Time.time < nextSmoke)) return;

        Vector3 centre = leftEye && rightEye ? (leftEye.position + rightEye.position)*.5f :
            head ? head.position + Vector3.up*.08f : owner.HeadMesh.bounds.center;
        Vector3 right = Vector3.ProjectOnPlane(leftArm && rightArm ? rightArm.position-leftArm.position : transform.right, Vector3.up).normalized;
        if (right.sqrMagnitude < .5f) right = Vector3.right;
        float span = leftArm && rightArm ? Mathf.Clamp(Vector3.Distance(leftArm.position,rightArm.position)*.55f, .14f, .27f) : .18f;
        if (Time.time >= nextMark)
        {
            Vector3 side = right * (sequence % 2 == 0 ? 1 : -1);
            Vector3 origin = centre + side * span * Range(1,1.15f);
            float interval;
            float size = Mathf.Lerp(.78f,1.12f,strength);
            switch (emotion)
            {
                case "HAPPY":
                    Emit(marks, origin + Vector3.up*Range(-.14f,.04f), Vector3.up*.068f, .052f*size, 1.05f, new Color(1,.84f,.32f), Range(-15,15));
                    interval = Mathf.Lerp(.56f,.19f,strength); break;
                case "SAD":
                    Emit(marks, origin + Vector3.up*Range(.02f,.10f), Vector3.down*.155f, .047f*size, 1.3f, new Color(.36f,.74f,1,.9f));
                    interval = Mathf.Lerp(.52f,.17f,strength); break;
                case "ANGRY":
                    for (int i = 0; i < (strength >= .55f ? 3 : 1); i++)
                        Emit(marks, origin - Vector3.up*.14f, Vector3.up*Range(.16f,.32f)+side*Range(-.028f,.065f), .05f*size, Range(.45f,.72f), new Color(1,Range(.33f,.74f),.13f),Range(-22,22));
                    interval = Mathf.Lerp(.55f,.17f,strength); break;
                case "NERVOUS":
                    for (int i=0;i<2;i++)
                        Emit(marks, origin - Vector3.up*(.045f+.029f*i), side*.026f, .087f*size, .60f, new Color(1,.76f,.2f),Range(-9,9));
                    interval = Mathf.Lerp(.90f,.43f,strength); break;
                case "THINKING":
                    int bubble = sequence%3;
                    Emit(marks, centre + right*(span+.025f*bubble) + Vector3.up*(.04f+.048f*bubble), Vector3.up*.025f,
                        (.025f+.017f*bubble)*size, 1.45f, new Color(.82f,.68f,1,.88f));
                    interval = bubble == 2 ? Mathf.Lerp(1.6f,1f,strength) : .23f; break;
                default:
                    Emit(marks, origin-Vector3.up*.13f, Vector3.up*.023f, .024f, 1.55f, new Color(.92f,.95f,1,.42f));
                    interval = 1.75f; break;
            }
            sequence++;
            nextMark = Time.time + interval;
        }
        if (smoky && Time.time >= nextSmoke)
        {
            if (!smoke.isPlaying) smoke.Play(false);
            Vector3 side = right * (random.Next(2)==0 ? 1 : -1);
            Emit(smoke, centre + side*span - Vector3.up*.12f, Vector3.up*.125f+side*.025f, .13f, 1.4f, new Color(.64f,.63f,.63f,.57f),Range(-45,45));
            nextSmoke = Time.time + Mathf.Lerp(.7f,.35f,strength);
        }
    }

    private float Range(float min, float max) => Mathf.Lerp(min,max,(float)random.NextDouble());

    private static void Emit(ParticleSystem system, Vector3 position, Vector3 velocity, float size, float lifetime, Color color, float rotation=0)
    {
        system.Emit(new ParticleSystem.EmitParams { position=position, velocity=velocity, startSize=size,
            startLifetime=lifetime, startColor=color, rotation=rotation }, 1);
    }

    private void OnDestroy()
    {
        if (marks) Destroy(marks.gameObject);
        if (smoke) Destroy(smoke.gameObject);
    }
}
