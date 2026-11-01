using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace AimTrainer
{
    // Angles are degrees (yaw, pitch). Sizes are a world radius at a distance in meters;
    // at 10 m, r = 0.1 covers about 0.57°.
    public abstract class Scenario
    {
        public string id, name, tag, desc;
        public float dur = 60f;
        public bool speed;   // single-target drills: faster kills score more
        public bool track;   // hold-to-track drill instead of clicking

        public abstract void Begin(Game g);
        public virtual void OnHit(Game g, Target t) { }
        public virtual void OnExpire(Game g, Target t) { }
        public virtual void Tick(Game g, float dt) { }

        public static readonly string[] Routine =
        {
            "gridshot", "spidershot", "microshot", "frenzy", "frenzyflick", "sixshot",
            "reflexshot", "headline", "motionshot", "strafeshot", "strafetrack"
        };

        public static List<Scenario> All() => new List<Scenario>
        {
            new Gridshot(), new Spidershot(), new Microshot(), new Frenzy(), new FrenzyFlick(), new Sixshot(),
            new Reflexshot(), new HeadshotLine(), new Motionshot(), new Strafeshot(), new Strafetrack()
        };
    }

    class Gridshot : Scenario
    {
        static readonly float[] Y = { -12, -6, 0, 6, 12 }, P = { -3, 2, 7, 12 };
        Vector2? lastCell;

        public Gridshot()
        {
            id = "gridshot"; name = "Gridshot"; tag = "Warm-up";
            desc = "Three targets on a grid. Get your hand moving and work on clean target switching.";
        }
        public override void Begin(Game g) { lastCell = null; for (int i = 0; i < 3; i++) SpawnOne(g); }
        void SpawnOne(Game g)
        {
            var free = new List<Vector2>();
            foreach (var y in Y)
                foreach (var p in P)
                {
                    var c = new Vector2(y, p);
                    if (lastCell == c || g.targets.Any(t => t.yaw == y && t.pitch == p)) continue;
                    free.Add(c);
                }
            var pick = free[Random.Range(0, free.Count)];
            g.Spawn(pick.x, pick.y, .42f, 10f);
        }
        public override void OnHit(Game g, Target t) { lastCell = new Vector2(t.yaw, t.pitch); SpawnOne(g); }
    }

    class Spidershot : Scenario
    {
        public Spidershot()
        {
            id = "spidershot"; name = "Spidershot"; tag = "Flick"; speed = true;
            desc = "Flick out to a random target, then snap back to center. The core Valorant flick drill.";
        }
        public override void Begin(Game g) => Center(g);
        void Center(Game g) => g.Spawn(0, 3, .3f, 10f).center = true;
        public override void OnHit(Game g, Target t)
        {
            if (!t.center) { Center(g); return; }
            var p = g.RandPos(new Vector4(-34, 34, -5, 15), new List<Vector2> { new Vector2(0, 3) }, 10);
            g.Spawn(p.x, p.y, .3f, 10f);
        }
    }

    class Microshot : Scenario
    {
        public Microshot()
        {
            id = "microshot"; name = "Microshot"; tag = "Micro"; speed = true;
            desc = "One tiny target that respawns close to the last one. Trains the small corrections after a flick.";
        }
        public override void Begin(Game g) => g.Spawn(0, 3, .09f, 10f);
        public override void OnHit(Game g, Target t)
        {
            float a = Random.Range(0f, Mathf.PI * 2), d = Random.Range(1.2f, 4.2f);
            float y = t.yaw + Mathf.Cos(a) * d, p = t.pitch + Mathf.Sin(a) * d;
            if (y < -14 || y > 14) y = t.yaw - Mathf.Cos(a) * d;
            if (p < -4 || p > 11) p = t.pitch - Mathf.Sin(a) * d;
            g.Spawn(y, p, .09f, 10f);
        }
    }

    class Frenzy : Scenario
    {
        float next;
        public Frenzy()
        {
            id = "frenzy"; name = "Frenzy"; tag = "Small balls";
            desc = "Tiny balls keep spawning in a tight area. Clear them faster than they appear.";
        }
        public override void Begin(Game g) { next = 0; for (int i = 0; i < 3; i++) SpawnOne(g); }
        void SpawnOne(Game g)
        {
            var p = g.RandPos(new Vector4(-16, 16, -4, 11), g.TargetPoints(), 2.6f);
            g.Spawn(p.x, p.y, .15f, 10f);
        }
        public override void Tick(Game g, float dt)
        {
            if (g.time < next) return;
            if (g.targets.Count < 9) SpawnOne(g);
            next = g.time + .42f;
        }
    }

    class FrenzyFlick : Scenario
    {
        float next;
        public FrenzyFlick()
        {
            id = "frenzyflick"; name = "Frenzy Flick"; tag = "Small balls";
            desc = "Small balls spawn far apart across a wide area. Long, committed flicks onto tiny targets.";
        }
        public override void Begin(Game g) { next = 0; for (int i = 0; i < 2; i++) SpawnOne(g); }
        void SpawnOne(Game g)
        {
            var avoid = g.targets.Count > 0 ? g.TargetPoints() : new List<Vector2> { g.AimPoint };
            var p = g.RandPos(new Vector4(-38, 38, -6, 16), avoid, 16);
            g.Spawn(p.x, p.y, .19f, 10f);
        }
        public override void Tick(Game g, float dt)
        {
            if (g.time < next) return;
            if (g.targets.Count < 4) SpawnOne(g);
            next = g.time + .55f;
        }
    }

    class Sixshot : Scenario
    {
        public Sixshot()
        {
            id = "sixshot"; name = "Sixshot"; tag = "Switching";
            desc = "Six small targets at once. Fast target switching. Keep accuracy at 85% or higher.";
        }
        public override void Begin(Game g) { for (int i = 0; i < 6; i++) SpawnOne(g); }
        void SpawnOne(Game g)
        {
            var p = g.RandPos(new Vector4(-18, 18, -4, 11), g.TargetPoints(), 3.8f);
            g.Spawn(p.x, p.y, .2f, 10f);
        }
        public override void OnHit(Game g, Target t) => SpawnOne(g);
    }

    class Reflexshot : Scenario
    {
        public Reflexshot()
        {
            id = "reflexshot"; name = "Reflexshot"; tag = "Reaction"; speed = true;
            desc = "A target pops up anywhere for 0.85 s. React and click it before it disappears.";
        }
        public override void Begin(Game g) => SpawnOne(g);
        void SpawnOne(Game g)
        {
            var p = g.RandPos(new Vector4(-40, 40, -5, 14), new List<Vector2> { g.AimPoint }, 12);
            g.Spawn(p.x, p.y, .3f, 10f).life = .85f;
        }
        public override void OnHit(Game g, Target t) => g.Later(.22f, () => SpawnOne(g));
        public override void OnExpire(Game g, Target t) => g.Later(.22f, () => SpawnOne(g));
    }

    class HeadshotLine : Scenario
    {
        float lastYaw;
        public HeadshotLine()
        {
            id = "headline"; name = "Headshot Line"; tag = "Crosshair placement"; speed = true;
            desc = "Heads appear at eye level from 8 to 26 m away, like peeks on a real map. Keep your crosshair at head height.";
        }
        public override void Begin(Game g) { lastYaw = 0; SpawnOne(g); }
        void SpawnOne(Game g)
        {
            float y;
            do { y = Random.Range(-50f, 50f); } while (Mathf.Abs(y - lastYaw) < 15);
            lastYaw = y;
            float dist = Random.Range(8f, 26f), headY = .05f + Random.Range(-.05f, .12f);
            var t = g.Spawn(y, Mathf.Atan2(headY, dist) * Mathf.Rad2Deg, .13f, dist);
            t.life = 1.4f;
            g.AddBody(t);
        }
        public override void OnHit(Game g, Target t) => g.Later(.18f, () => SpawnOne(g));
        public override void OnExpire(Game g, Target t) => g.Later(.18f, () => SpawnOne(g));
    }

    class Motionshot : Scenario
    {
        public Motionshot()
        {
            id = "motionshot"; name = "Motionshot"; tag = "Moving";
            desc = "Three targets drift and bounce around. Click them on the move, like enemies running between cover.";
        }
        public override void Begin(Game g) { for (int i = 0; i < 3; i++) SpawnOne(g); }
        void SpawnOne(Game g)
        {
            var p = g.RandPos(new Vector4(-24, 24, -3, 12), g.TargetPoints(), 6);
            float a = Random.Range(0f, Mathf.PI * 2), s = Random.Range(6f, 14f);
            var t = g.Spawn(p.x, p.y, .3f, 12f);
            t.vy = Mathf.Cos(a) * s; t.vp = Mathf.Sin(a) * s * .6f;
            t.bounds = new Vector4(-26, 26, -4, 13);
        }
        public override void OnHit(Game g, Target t) => SpawnOne(g);
    }

    class Strafeshot : Scenario
    {
        public Strafeshot()
        {
            id = "strafeshot"; name = "Strafeshot"; tag = "ADAD"; speed = true;
            desc = "A small target ADAD-strafes and changes direction at random. Read the strafe and tap.";
        }
        public override void Begin(Game g) => SpawnOne(g);
        void SpawnOne(Game g)
        {
            var p = g.RandPos(new Vector4(-24, 24, -1, 9), new List<Vector2> { g.AimPoint }, 10);
            var t = g.Spawn(p.x, p.y, .22f, 12f);
            t.strafe = new Vector2(12, 22);
            t.vy = (Random.value < .5f ? -1 : 1) * Random.Range(12f, 22f);
            t.switchAt = g.time + Random.Range(.25f, .8f);
            t.bounds = new Vector4(-28, 28, -2, 10);
        }
        public override void OnHit(Game g, Target t) => SpawnOne(g);
    }

    class Strafetrack : Scenario
    {
        public Strafetrack()
        {
            id = "strafetrack"; name = "Strafetrack"; tag = "Tracking"; dur = 45f; track = true;
            desc = "Hold left mouse and stay on a strafing target. Short tracking block for moving enemies and spray transfers.";
        }
        public override void Begin(Game g)
        {
            var t = g.Spawn(0, 3, .42f, 12f);
            t.strafe = new Vector2(10, 24);
            t.vy = 16;
            t.switchAt = Random.Range(.3f, 1f);
            t.bounds = new Vector4(-30, 30, -2, 10);
            t.bob = true; t.bobPhase = Random.Range(0f, 6f);
        }
    }
}
