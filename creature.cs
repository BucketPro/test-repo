using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class Creature : MonoBehaviour, IDamaging
{
    // Movement
    public bool isFlipped = false, isMoving = false;
    public Vector2 pos;
    public float baseSpd;
    public Control cntr;
    public Vector2 collOffset, collSize;

    // Air Travel & Knockback
    protected bool inAir;
    float airTime, airQuadA, airQuadB;
    int airPrio;
    public delegate void OnLand();
    OnLand landFunc;

    // Animation
    Animator[] anims;
    SpriteRenderer[] sprites;
    Dictionary<string, int[]> animParamList;

    // Stats
    public int maxHp;
    public int hp;
    HealthBar hpBar;
    public float healthBarOffset;
    bool isDead;

    // Attacking
    public GameObject[] hitBoxes;
    protected Hitbox hitBox;
    Hitbox.HBox[] hitboxData;

    // Buffs
    protected class Stats
    {
        public bool slowLess;

        public float spdMod;
    }
    protected class Buff
    {
        public float dur;
        public delegate void Effect(Stats s);
        public Effect eff;

        public Buff(float dur_, Effect eff_)
        {
            dur = dur_;
            eff = eff_;
        }
    }
    protected Stats stats;
    List<Buff> buffs;

    void Start()
    {
        Setup();
    }

    void Update()
    {
        if (isDead)
        {
            if (this is Player) (this as Player).PanToPlayer();
            return;
        }
        PerFrame();
        foreach (SpriteRenderer sprite in sprites) sprite.flipX = isFlipped;

        if (inAir) AirHeight();
        else AI();

        pos = cntr.transform.position;
    }

    protected virtual void Setup()
    {
        // Hitboxes
        hitboxData = new Hitbox.HBox[hitBoxes.Length];
        for (int i = 0; i < hitBoxes.Length; i++)
        {
            HitboxData hbox = hitBoxes[i].GetComponent<HitboxData>();
            Vector2? set = null;
            if (hbox.dir) set = hbox.kbDir;
            hitboxData[i] = new Hitbox.HBox(hbox.dmg, hbox.kbPower, hbox.stunDur, set, hitBoxes[i].GetComponent<PolygonCollider2D>().points);
        }

        // Animator
        anims = GetComponentsInChildren<Animator>();
        sprites = GetComponentsInChildren<SpriteRenderer>();
        animParamList = new Dictionary<string, int[]>();

        // Health
        hp = maxHp;
        hpBar = (Instantiate(Resources.Load("Prefabs/Handling/HealthBar"), gameObject.transform) as GameObject).GetComponent<HealthBar>();
        hpBar.gameObject.transform.localPosition = Vector2.up * healthBarOffset;

        // Collider
        cntr = Control.Create(this, collOffset, collSize);

        // Stats & Buffs
        stats = new Stats();
        ResetStats();
        buffs = new List<Buff>();
    }
    protected virtual void PerFrame()
    {
        CheckBuffs();
        UpdateGrfx();
    }
    protected virtual void AI() { }
    protected virtual void Die()
    {
        Destroy(cntr.gameObject);
        gameObject.GetComponent<BoxCollider2D>().enabled = false;
        SetAnimBool("Dead", true);
        isDead = true;
    }
    protected virtual void ModHBox(int box) { }
    protected virtual bool WhenHit(Hitbox.HitData data) { return true; }
    public virtual void OnHit(Creature target) { }

    // Getting Hit
    public void GetHit(Hitbox.HitData data)
    {
        if (!WhenHit(data)) return;
        ResetAttack();

        // Damage
        hp -= data.dmg;
        hpBar.SetHealth((float)hp / maxHp);
        if (hp <= 0)
        {
            if (data.stunDur < 0.1f) data.stunDur = 0.1f;
            hp = 0;
        }

        // Visuals
        isFlipped = data.atkPos.x < pos.x;
        foreach (SpriteRenderer sprite in sprites) sprite.color = Color.red;
        SetAir(data.stunDur, 0.25f, data.kbDir.normalized * data.kbPower, false, KBLanding, 1);
    }
    void KBLanding()
    {
        foreach (SpriteRenderer sprite in sprites) sprite.color = Color.white;
        if (hp <= 0) Die();
        SetAnimBool("reset", false);
    }
    protected virtual void ResetAttack()
    {
        cntr.SetColl(true);
        SetAnimBool("reset", true);
        SetHitbox(-1);
        inAir = false;
    }
    public void Cleanup()
    {
        if (cntr != null) Destroy(cntr.gameObject);
        if (hpBar != null) Destroy(hpBar.gameObject);
        Destroy(gameObject);
    }

    // Stats & Buffs
    public void ResetStats()
    {
        stats.slowLess = false;

        stats.spdMod = 1;
    }
    protected void AddBuff(float dur, Buff.Effect eff)
    {
        buffs.Add(new Buff(dur, eff));
        eff(stats);
    }
    void CheckBuffs()
    {
        bool refresh = false;
        for(int i = buffs.Count - 1; i >= 0; i--)
        {
            if((buffs[i].dur -= Time.deltaTime) < 0)
            {
                refresh = true;
                buffs.RemoveAt(i);
            }
        }
        if (!refresh) return;
        ResetStats();
        foreach (Buff buff in buffs) buff.eff(stats);
    }

    // Show Graphical State & Position
    protected void UpdateGrfx()
    {
        SetAnimBool("isMoving", isMoving);
        transform.position = cntr.transform.position;
        foreach (SpriteRenderer sprite in sprites) sprite.sortingOrder = (int)(transform.position.y * -1000);
        GetComponent<SpriteRenderer>().sortingOrder += 1;
    }
    protected void SetSpriteVision(bool val)
    {
        foreach (SpriteRenderer sprite in sprites) sprite.enabled = val;
    }

    // Air Travel
    void SetInAir(bool val)
    {
        gameObject.GetComponent<SpriteRenderer>().sortingLayerName = val ? "InAir" : "CrtWall";
    }
    protected void SetAir(float airTime_, float airHeight, Vector2 airVelo, bool passOver, OnLand func, int prio)
    {
        if (inAir && prio > airPrio) return;
        airPrio = prio;
        SetInAir(inAir = true);
        airTime = airTime_;
        airQuadB = 4 * airHeight / airTime;
        airQuadA = -1 * airQuadB / airTime;
        cntr.SetVel(airVelo);
        cntr.SetColl(!passOver);
        landFunc = func;
    }
    void AirHeight()
    {
        if (inAir = ((airTime -= Time.deltaTime) > 0))
        {
            float hght = airQuadA * airTime * airTime + airQuadB * airTime;
            transform.position += new Vector3(0, hght, 0);
        }
        else
        {
            airTime = 0;
            cntr.SetVel(Vector2.zero);
            cntr.SetColl(true);
            landFunc();
            SetInAir(false);
        }
    }

    // Hitbox Activation Control
    public void SetHitbox(int box)
    {
        if (!(hitBox.gameObject.GetComponent<PolygonCollider2D>().enabled = box != -1)) return;
        hitBox.gameObject.GetComponent<PolygonCollider2D>().points = hitboxData[box + (isFlipped ? 1 : 0)].points;
        hitBox.data = hitboxData[box + (isFlipped ? 1 : 0)];
        ModHBox(box);
    }
    Vector2 IDamaging.Pos
    {
        get
        {
            return pos;
        }
    }
    Transform IDamaging.Transform
    {
        get
        {
            return transform;
        }
    }

    // Check Animator Parameters
    int[] AnimContains(string name)
    {
        if (animParamList.ContainsKey(name)) return animParamList[name];
        List<int> ints = new List<int>();
        for(int i = 0; i < anims.Length; i++) foreach (AnimatorControllerParameter param in anims[i].parameters) if (param.name == name) ints.Add(i);
        animParamList.Add(name, ints.ToArray());
        return animParamList[name];
    }

    // Set Animator Values
    protected bool SetAnimInt(string name, int value)
    {
        if (AnimContains(name).Length == 0) return false;
        foreach(int val in animParamList[name]) anims[val].SetInteger(name, value);
        return true;
    }
    protected bool SetAnimBool(string name, bool value)
    {
        if (AnimContains(name).Length == 0) return false;
        foreach (int val in animParamList[name]) anims[val].SetBool(name, value);
        return true;
    }
    protected bool SetAnimFloat(string name, float value)
    {
        if (AnimContains(name).Length == 0) return false;
        foreach (int val in animParamList[name]) anims[val].SetFloat(name, value);
        return true;
    }
    protected bool SetAnimTrig(string name, bool value)
    {
        if (AnimContains(name).Length == 0) return false;
        if (value) foreach (int val in animParamList[name]) anims[val].SetTrigger(name);
        else foreach (int val in animParamList[name]) anims[val].ResetTrigger(name);
        return true;
    }
}
