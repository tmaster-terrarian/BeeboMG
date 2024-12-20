using System;
using System.Collections.Generic;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

using Beebo.Graphics;

using Jelly;
using Jelly.Components;
using Jelly.Graphics;
using Jelly.Utilities;
using Beebo.GameContent;

namespace Beebo.Components;

public enum PlayerState
{
    IgnoreState,
    StandIdle,
    Normal,
    Dead,
    LedgeGrab,
    LedgeClimb,
    Wallslide,
}

public class Player : Actor
{
    private static readonly Random nugdeRandom = new();

    private PlayerState _state = PlayerState.Normal; // please do NOT touch this thx
    private bool _stateJustChanged;

    private readonly float gravity = 0.2f;
    private readonly float baseMoveSpeed = 2;
    private readonly float baseJumpSpeed = -3.7f;
    private readonly float baseGroundAcceleration = 0.10f;
    private readonly float baseGroundFriction = 0.14f;
    private readonly float baseAirAcceleration = 0.07f;
    private readonly float baseAirFriction = 0.02f;
    private readonly int baseBulletDelay = 6;
    private readonly int baseBombDelay = 90;

    private float moveSpeed;
    private float jumpSpeed;
    private float accel;
    private float fric;

    private int jumpBuffer;
    private int inputDir;
    private bool jumpCancelled;
    private bool running;
    private bool skidding;
    private bool wasOnGround;
    private bool onJumpthrough;

    private Vector2 oldVelocity;

    private float duck;

    private bool canJump = true;
    private bool canWalljump = true;
    private bool canLedgeGrab = true;

    private bool fxTrail;
    private int fxTrailCounter;
    private readonly List<AfterImage> afterImages = [];

    private readonly Dictionary<string, AudioDef> sounds = [];

    private readonly List<Texture2D> textures = [];
    private readonly List<int> frameCounts = [];
    private TextureIndex textureIndex;
    private float frame;

    private float squash = 1; // vertical
    private float stretch = 1; // horizontal

    private float recoil;
    private float gunAngle;
    private Point gunOffset;
    private int bulletDelay;
    private int bombDelay;

    // TIMERS
    private int ledgegrabTimer;
    private int landTimer;
    private int wallslideTimer;

    private Rectangle MaskNormal = new(0, -14, 8, 14);
    private Rectangle MaskDuck = new(0, -8, 8, 8);
    private Rectangle MaskLedge = new(-4, 0, 8, 14);
    private Point PivotNormal = new(0, 24);
    private Point PivotDuck = new(0, 24);
    private Point PivotLedge = new(7, 12);

    private Solid platformTarget = null;

    public float Lookup { get; private set; }

    public float CurrentMoveSpeed => moveSpeed;

    class AfterImage
    {
        public float Alpha = 1;
        public TextureIndex TextureIndex;
        public int Frame;
        public Vector2 Position;
        public int Facing;
        public Vector2 Scale = Vector2.One;
        public Color Color = Color.White;
        public float Rotation;
        public SpriteEffects SpriteEffects = SpriteEffects.None;
        public Vector2 Pivot;
    }

    public enum TextureIndex
    {
        Idle,
        LookUp,
        Crawl,
        Duck,
        Dead,
        Jump,
        Run,
        RunFast,
        Wallslide,
        LedgeGrab,
        LedgeClimb
    }

    public PlayerInputMapping InputMapping { get; } = new() {
        Left = new MappedInput.Keyboard(Keys.A),
        Right = new MappedInput.Keyboard(Keys.D),
        Up = new MappedInput.Keyboard(Keys.W),
        Down = new MappedInput.Keyboard(Keys.S),
    };

    public PlayerState State {
        get => _state;
        set {
            if(_state != value)
            {
                _stateJustChanged = true;

                OnStateExit(_state);
                OnStateEnter(value);

                _state = value;
            }
        }
    }

    bool flipGun = false;

    public bool FaceTowardsMouse { get; private set; } = true;

    public int VisualFacing {
        get {
            if(State != PlayerState.Normal) return Facing;

            int facing = FaceTowardsMouse ? Math.Sign(Main.Camera.MousePositionInWorld.X - Center.X) : Facing;
            if(facing == 0) facing = Facing;
            return facing;
        }
    }

    public bool UseGamePad { get; set; }

    public int Hp { get; set; } = 100;
    public bool Dead { get; private set; }

    private bool onCreated;

    public override void OnCreated()
    {
        onCreated = true;

        AddTexture("idle", 6);
        AddTexture("lookup", 6);
        AddTexture("crawl", 8);
        AddTexture("duck", 4);
        AddTexture("dead", 3);
        AddTexture("jump", 6);
        AddTexture("run", 8);
        AddTexture("run_fast", 6);
        AddTexture("wallslide");
        AddTexture("ledgegrab");
        AddTexture("ledgeclimb", 3);

        AddSound("jump");
        AddSound("wall_jump");
        AddSound("land");
        AddSound("shoot");
        AddSound("throw_bomb");

        SetHitbox(MaskNormal, PivotNormal);

        onCreated = false;
    }

    void AddTexture(string path, int frameCount = 1)
    {
        if(!onCreated) throw new InvalidOperationException("Can only add textures in the OnCreated method");

        const string texPath = "Images/Player/";
        textures.Add(ContentLoader.LoadTexture(texPath + path));
        frameCounts.Add(frameCount);
    }

    void AddSound(string path)
    {
        if(!onCreated) throw new InvalidOperationException("Can only add sounds in the OnCreated method");

        const string texPath = "player_";
        sounds.Add(path, AudioRegistry.GetDefStatic(texPath + path));
    }

    void SetHitbox(Rectangle mask, Point pivot)
    {
        bboxOffset = new Point(-4 + mask.X * Facing, mask.Y);
        Width = mask.Width;
        Height = mask.Height;
        Entity.Pivot = new Point(12 + pivot.X * Facing, pivot.Y);
    }

    public override void Update()
    {
        wasOnGround = OnGround;
        onJumpthrough = CheckCollidingJumpthrough(BottomEdge.Shift(0, 1));
        if(onJumpthrough) OnGround = true;
        else OnGround = CheckColliding(BottomEdge.Shift(0, 1));

        if(!wasOnGround && OnGround)
        {
            Grounded();
        }

        if(textureIndex == TextureIndex.LookUp || textureIndex == TextureIndex.Idle)
            frame += 0.2f;

        running = textureIndex == TextureIndex.Run || textureIndex == TextureIndex.RunFast;

        if(State != PlayerState.LedgeClimb)
            ledgegrabTimer = MathUtil.Approach(ledgegrabTimer, 0, 1);

        inputDir = InputMapping.Right.IsDown.ToInt32() - InputMapping.Left.IsDown.ToInt32();

        if(OnGround && State != PlayerState.LedgeGrab)
        {
            platformTarget = Scene.CollisionSystem.SolidPlace(Hitbox.Shift(0, 2));
        }
        else if(Scene.CollisionSystem.SolidMeeting(Hitbox.Shift(2 * inputDir, 0)) && (State == PlayerState.Normal || State == PlayerState.Wallslide))
        {
            platformTarget = Scene.CollisionSystem.SolidPlace(Hitbox.Shift(2 * inputDir, 0));
        }
        else if(jumpBuffer < 10 && State != PlayerState.LedgeGrab)
        {
            platformTarget = null;
        }

        RecalculateStats();

        if(!OnGround)
            duck = 0;
        if(duck > 0)
        {
            moveSpeed *= 0.5f;
            if(Math.Abs(velocity.X) > moveSpeed)
                velocity.X = MathUtil.Approach(velocity.X, moveSpeed * inputDir, 0.25f);
        }

        StateUpdate();

        #region Jump Logic
        if(InputMapping.Jump.Pressed && canJump)
        {
            if(OnGround || (jumpBuffer > 0 && velocity.Y > 0))
            {
                platformTarget = null;
                // var s = null;
                SoundEffectInstance s = null;
                if(duck <= 0)
                {
                    State = PlayerState.Normal;
                    frame = 0;
                    textureIndex = TextureIndex.Jump;
                    var c = Scene.CollisionSystem.SolidPlace(new Rectangle(Center.X, Bottom + 1, 1, 1));
                    if(c is not null)
                    {
                        oldVelocity.X = c.velocity.X;
                        oldVelocity.Y = c.velocity.Y;
                        velocity.X += c.velocity.X;
                        if(c.velocity.Y < 0)
                            velocity.Y = c.velocity.Y;
                    }
                    velocity.Y = jumpSpeed;
                    s = sounds["jump"].Play();
                }
                else if(!CheckColliding(Hitbox.Shift(0, -2)))
                {
                    State = PlayerState.Normal;
                    var c = Scene.CollisionSystem.SolidPlace(new Rectangle(Center.X, Bottom + 1, 1, 1));
                    if(c is not null)
                    {
                        oldVelocity.X = c.velocity.X;
                        oldVelocity.Y = c.velocity.Y;
                        velocity.X += c.velocity.X;
                        if(c.velocity.Y < 0)
                            velocity.Y = c.velocity.Y;
                    }
                    velocity.Y += jumpSpeed / 2;
                    s = sounds["jump"].Play();
                }
                else
                {
                    s = sounds["jump"].Play();
                }

                if(!OnGround)
                {
                    if(CheckColliding(Hitbox.Shift((int)moveSpeed, 0), true) && canWalljump)
                    {
                        State = PlayerState.Normal;
                        velocity.X = -moveSpeed;
                        velocity.Y = jumpSpeed;
                        Facing = -1;
                        var w = Scene.CollisionSystem.SolidPlace(Hitbox.Shift((int)moveSpeed, 0));
                        if(w is not null)
                            velocity.X += w.velocity.X / 2;
                        s?.Stop();
                        sounds["wall_jump"].Play();
                    }
                    else if(CheckColliding(Hitbox.Shift((int)-moveSpeed, 0), true) && canWalljump)
                    {
                        State = PlayerState.Normal;
                        velocity.X = moveSpeed;
                        velocity.Y = jumpSpeed;
                        Facing = 1;
                        var w = Scene.CollisionSystem.SolidPlace(Hitbox.Shift((int)-moveSpeed, 0));
                        if(w is not null)
                            velocity.X += w.velocity.X / 2;
                        s?.Stop();
                        sounds["wall_jump"].Play();
                    }
                    else if(jumpBuffer > 0 && velocity.Y > 0)
                    {
                        jumpBuffer = 0;
                        for (var i = 0; i < 4; i++)
                        {
                            // var p = instance_create_depth((bbox_left + random(8)), random_range(bbox_bottom, bbox_bottom), (depth - 1), fx_dust);
                            //     p.textureIndex = spr_fx_dust2;
                            //     p.vx = random_range(-0.5, 0.5);
                            //     p.vz = random_range(-0.2, 0);
                        }
                    }
                }
            }
            else if(State == PlayerState.LedgeGrab || State == PlayerState.LedgeClimb)
            {
                ledgegrabTimer = 15;
                Collides = true;
                var c = platformTarget;
                if(c is not null)
                {
                    oldVelocity.X = c.velocity.X;
                    oldVelocity.Y = c.velocity.Y;
                    velocity.X = c.velocity.X;
                    if(c.velocity.Y < 0)
                        velocity.Y = c.velocity.Y;
                }

                SetHitbox(MaskNormal, PivotNormal);

                if(inputDir != 0) // if input then jump off with some horizontal speed
                {
                    velocity.X += moveSpeed * 0.8f * inputDir + (0.4f * -Facing);

                    if(!CheckColliding(Hitbox.Shift(0, c is not null ? c.Top - Top : 0), c is null) && inputDir == Facing) // if theres space jump as normal
                        velocity.Y -= 2.7f * (!InputMapping.Down.IsDown).ToInt32();
                    else // else displace the player first
                    {
                        if(State == PlayerState.LedgeGrab)
                            Entity.X -= 4 * Facing;
                        Entity.Y += 12;
                        velocity.Y -= 2.7f * (!InputMapping.Down.IsDown).ToInt32();
                        textureIndex = TextureIndex.Jump;
                        frame = 0;
                    }
                    sounds["wall_jump"].Play();
                }
                else // otherwise just hop off
                {
                    if(State == PlayerState.LedgeGrab)
                        Entity.X -= 4 * Facing;
                    Entity.Y += 12;
                    velocity.Y -= 2.7f * (!InputMapping.Down.IsDown).ToInt32();
                    textureIndex = TextureIndex.Jump;
                    frame = 0;
                    sounds["wall_jump"].Play();
                }
                State = PlayerState.Normal;
            }
            else if(canWalljump)
            {
                if(CheckColliding(Hitbox.Shift((int)moveSpeed, 0), true))
                {
                    platformTarget = null;
                    State = PlayerState.Normal;
                    velocity.X = -moveSpeed;
                    velocity.Y = jumpSpeed;
                    Facing = -1;
                    var w = Scene.CollisionSystem.SolidPlace(Hitbox.Shift((int)moveSpeed, 0));
                    if(w is not null)
                        velocity.X += w.velocity.X / 2;
                    sounds["wall_jump"].Play();
                }
                else if(CheckColliding(Hitbox.Shift((int)-moveSpeed, 0), true))
                {
                    platformTarget = null;
                    State = PlayerState.Normal;
                    velocity.X = moveSpeed;
                    velocity.Y = jumpSpeed;
                    Facing = 1;
                    var w = Scene.CollisionSystem.SolidPlace(Hitbox.Shift((int)-moveSpeed, 0));
                    if(w is not null)
                        velocity.X += w.velocity.X / 2;
                    sounds["wall_jump"].Play();
                }
            }
        }

        if(InputMapping.Jump.Released && velocity.Y < 0 && !jumpCancelled)
        {
            jumpCancelled = true;
            velocity.Y /= 2;
        }
        #endregion

        if(velocity.Y > 4)
        {
            squash = MathHelper.Clamp(1.01f * (velocity.Y / 12), 1, 1.4f);
            stretch = MathHelper.Clamp(0.99f / (velocity.Y / 12), 0.65f, 1);
        }

        if(skidding && OnGround && duck == 0)
        {
            textureIndex = TextureIndex.Run;
            frame = 6;

            // with(instance_create_depth(x, bbox_bottom, (depth - 10), fx_dust))
            // {
            //     sprite_index = spr_fx_dust2;
            //     image_index = irandom(1)
            //     vx = random_range(-0.1, 0.1);
            //     vy = random_range(-0.5, -0.1);
            //     vz = 0;
            // }
        }

        if(Input.GetDown(Keys.LeftControl))
        {
            BottomMiddle = Main.Camera.MousePositionInWorld;
            velocity = Vector2.Zero;
        }

        if(!float.IsNormal(velocity.X)) velocity.X = 0;
        if(!float.IsNormal(velocity.Y)) velocity.Y = 0;

        MoveX(velocity.X, () => {
            if(State == PlayerState.Dead)
            {
                velocity.X = -velocity.X * 0.9f;
            }
            else /*for(int j = 0; j < MathUtil.RoundToInt(MathHelper.Max(Time.DeltaTime * 60, 1)); j++)*/
            {
                if(inputDir != 0 && !CheckColliding(Hitbox.Shift(inputDir, -2)))
                {
                    if(CheckColliding(Hitbox.Shift(inputDir, 0), true))
                    {
                        MoveY(-2, null);
                        MoveX(inputDir * 2, null);
                    }
                }
                else
                {
                    // if (Math.Abs(velocity.X) >= 1)
                    // {
                    //     _audio_play_sound(sn_player_land, 0, false);
                    //     for (int i = 0; i < 3; i++)
                    //     {
                    //         with(instance_create_depth((x + (4 * sign(facing))), random_range((bbox_bottom - 12), (bbox_bottom - 2)), (depth - 1), fx_dust))
                    //         {
                    //             sprite_index = spr_fx_dust2;
                    //             vy = (Math.Abs(other.velocity.Y) > 0.6) ? other.velocity.Y * 0.5 : vy;
                    //             vz = 0;
                    //         }
                    //     }
                    // }
                    velocity.X = 0;
                    // break;
                }
            }
        });
        MoveY(velocity.Y, () => {
            if(State == PlayerState.Normal)
            {
                landTimer = 8;
                textureIndex = inputDir != 0 ? TextureIndex.Run : TextureIndex.Idle;
                frame = 0;
            }
            if (velocity.Y > 0.4f)
            {
                sounds["land"].Play();
                squash = 0.9f;
                stretch = 1.4f;
            }
            // if (velocity.Y > 0.2)
            // {
            //     for (var i = 0; i < 4; i++)
            //     {
            //         with (instance_create_depth((bbox_left + random(8)), random_range(bbox_bottom, bbox_bottom), (depth - 1), fx_dust))
            //         {
            //             sprite_index = spr_fx_dust2
            //             vx = other.velocity.X
            //             vz = 0
            //         }
            //     }
            // }
            if(!(InputMapping.Down.IsDown && CheckCollidingJumpthrough(BottomEdge.Shift(new(0, 1)))))
                velocity.Y = 0;
        });

        if(Top > Scene.Height + 16)
        {
            TopLeft = Point.Zero;
            velocity = Vector2.Zero;
        }

        if(fxTrail)
        {
            fxTrailCounter++;
            if(fxTrailCounter >= 2)
            {
                fxTrailCounter = 0;
                afterImages.Add(new AfterImage {
                    TextureIndex = textureIndex,
                    Frame = (int)frame,
                    Position = Entity.Position.ToVector2(),
                    Facing = Facing,
                    Scale = Vector2.One,
                    Color = Color.White,
                    Rotation = 0,
                    Pivot = Entity.Pivot.ToVector2(),
                    SpriteEffects = SpriteEffects,
                });
            }
        }
        else
        {
            fxTrailCounter = 0;
        }

        for(int i = 0; i < afterImages.Count; i++)
        {
            AfterImage image = afterImages[i];

            image.Alpha = MathHelper.Max(image.Alpha - (1/12f), 0);
            if(image.Alpha == 0)
            {
                afterImages.RemoveAt(i);
                i--;
            }
        }

        squash = MathUtil.Approach(squash, 1, 0.15f);
        stretch = MathUtil.Approach(stretch, 1, 0.1f);

        if(inputDir != 0 && running && CheckColliding(Hitbox.Shift(inputDir, 0), true))
        {
            textureIndex = TextureIndex.Idle;
        }

        UpdateGun();
    }

    private void UpdateGun()
    {
        if(bulletDelay > 0) bulletDelay--;
        if(bombDelay > 0) bombDelay--;

        int x = 0;
        int y = 0;

        switch(textureIndex)
        {
            case TextureIndex.Idle:
            case TextureIndex.LookUp: {
                x = -3;
                switch((int)frame % 6)
                {
                    case 0:
                    case 1:
                    case 2:
                        y = -6;
                        break;
                    case 3:
                    case 4:
                    case 5:
                        y = -7;
                        break;
                }
                break;
            }
            case TextureIndex.Run: {
                x = -3;
                switch((int)frame % 8)
                {
                    case 0:
                    case 3:
                    case 4:
                    case 7:
                        y = -6;
                        break;
                    case 1:
                    case 2:
                    case 5:
                    case 6:
                        y = -5;
                        break;
                }
                break;
            }
            case TextureIndex.RunFast: {
                x = -3;
                switch((int)frame % 6)
                {
                    case 0: x = -3; y = -6; break;
                    case 1: x = -2; y = -5; break;
                    case 2: x = -1; y = -6; break;
                    case 3: x = -0; y = -6; break;
                    case 4: x = -0; y = -5; break;
                    case 5: x = -1; y = -6; break;
                }
                break;
            }
            case TextureIndex.Crawl: {
                switch((int)frame % 8)
                {
                    case 0: x = -2; y = -2; break;
                    case 1: x = -4; y = -2; break;
                    case 2: x = -5; y = -2; break;
                    case 3: x = -4; y = -2; break;
                    case 4: x = -3; y = -3; break;
                    case 5: x = -1; y = -3; break;
                    case 6: x =  1; y = -2; break;
                    case 7: x =  0; y = -2; break;
                }
                break;
            }
            default: {
                if(State == PlayerState.Wallslide)
                {
                    x = 3;
                    y = -7;
                }
                else if(State == PlayerState.LedgeGrab)
                {
                    x = -4;
                    y = 3;
                }
                else if(State == PlayerState.LedgeClimb)
                {
                    x = -4;
                    y = -5;
                }
                else if(duck > 0)
                {
                    x = -3 + (int)(1/3f * duck);
                    y = -5 + (int)duck;
                }
                else
                {
                    x = -3;
                    y = -7;
                }
                break;
            }
        }
        gunOffset.X = x;
        gunOffset.Y = y;

        var vec = (Main.Camera.MousePositionInWorld - new Point(Entity.X + x * VisualFacing, Entity.Y + y)).ToVector2().SafeNormalize();
        gunAngle = MathF.Atan2(vec.Y, vec.X);
        if(!float.IsNormal(gunAngle)) gunAngle = 0;

        if(State == PlayerState.Wallslide || State == PlayerState.LedgeGrab)
        {
            if(Facing == 1)
            {
                gunAngle = MathHelper.ToRadians(MathHelper.Clamp(MathHelper.ToDegrees(MathHelper.WrapAngle(gunAngle + MathF.PI)), -90, 90)) - MathF.PI;
            }
            else
            {
                gunAngle = MathHelper.ToRadians(MathHelper.Clamp(MathHelper.ToDegrees(gunAngle), -90, 90));
            }
        }

        Entity bullet = null;
        SoundEffectInstance shootSound = null;

        recoil = MathHelper.Max(0, recoil - 1);
        if(InputMapping.PrimaryFire.IsDown && bulletDelay == 0 && !(InputMapping.SecondaryFire.IsDown && bombDelay == 0))
        {
            Main.Camera.SetShake(1, 5);
            recoil = 2;
            bulletDelay = baseBulletDelay;

            shootSound = sounds["shoot"].Play();

            // spawn boolets

            // with (instance_create_depth(x, y, depth - 3, oBullet))
            // {
            // 	parent = obj_player
            //     _team = team.player
            //     audio_play_sound(snShot, 1, false);

            //     speed = 12;
            //     direction = other.image_angle + random_range(-v, v);
            //     image_angle = direction;

            //     damage = obj_player.damage
            // }

            float randSpread = MathHelper.ToRadians((GlobalRandom.PlayerAttacks.NextSingle() * 6) - 3);

            Scene.Entities.Add(bullet = new Entity(new(Entity.X + x * VisualFacing + (int)(12 * MathF.Cos(gunAngle)), Entity.Y + y - 1 + (int)(12 * MathF.Sin(gunAngle)))) {
                    Components = {
                        new BulletProjectile {
                            Direction = gunAngle + randSpread,
                            velocity = new(12 * MathF.Cos(gunAngle + randSpread), 12 * MathF.Sin(gunAngle + randSpread)),
                            Owner = Entity.EntityID,
                            Damage = 1,
                            Team = Team.Player
                        },
                    },
                }
            );

            // spawn casing

            // with(instance_create_depth(x + lengthdir_x(4, image_angle), y + lengthdir_y(4, image_angle) - 1, depth - 5, fx_casing))
            // {
            //     image_yscale = other.image_yscale
            //     angle = other.image_angle
            //     dir = other.image_yscale
            //     hsp = -other.image_yscale * random_range(1, 1.5)
            //     vsp = -1 + random_range(-0.2, 0.1)
            // }

            int facing = VisualFacing * (State == PlayerState.Wallslide || State == PlayerState.LedgeGrab ? -1 : 1);

            Scene.Entities.Add(new Entity(new(Entity.X + x * VisualFacing + (int)(4 * MathF.Cos(gunAngle)), Entity.Y + y - 1 + (int)(4 * MathF.Sin(gunAngle)))) {
                Depth = Entity.Depth - 5,
                Components = {
                    new BulletCasing {
                        Angle = MathHelper.ToRadians(MathF.Round(MathHelper.ToDegrees(gunAngle) / 10) * 10),
                        ImageFacing = facing,
                        Facing = facing,
                        velocity = {
                            X = -facing * (GlobalRandom.Vfx.NextSingle() * 0.5f + 1),
                            Y = -1 + (GlobalRandom.Vfx.NextSingle() * 0.3f - 0.2f)
                        }
                    }
                }
            });
        }

        if(InputMapping.SecondaryFire.Pressed && bombDelay > 0)
        {
            foreach(var e in Scene.Entities.FindAllWithComponent<BombProjectile>())
            {
                e.GetComponent<BombProjectile>().Explode();
            }
        }

        if(InputMapping.SecondaryFire.IsDown && bombDelay == 0)
        {
            foreach(var e in Scene.Entities.FindAllWithComponent<BombProjectile>())
            {
                e.GetComponent<BombProjectile>().Explode();
            }

            Main.Camera.SetShake(2, 10);
            recoil = 4;
            bombDelay = baseBombDelay;

            sounds["throw_bomb"].Play();

            // spawn bombas

            // with (instance_create_depth(x + lengthdir_x(12, image_angle), y + lengthdir_y(12, image_angle) - 1, depth - 2, obj_bomb))
            // {
            //     direction = other.image_angle;
            //     hsp = lengthdir_x(2, direction) + (obj_player.hsp * 0.5) + ((obj_player.state == "grind") * -0.5);
            //     vsp = lengthdir_y(2, direction) + (obj_player.vsp * 0.25) - 1;
            //     if((vsp > 0.2) && (obj_player.state == "grind")) max_bounces = 0

            //     if(mouse_check_button(mb_left) || gamepad_button_check(0, gp_shoulderrb)) event_perform(ev_other, ev_user2);
            // }

            var angle = MathHelper.ToRadians(MathF.Round(MathHelper.ToDegrees(gunAngle) / 10) * 10);

            var bomb = new Entity(new Point(
                Entity.X + x * VisualFacing + (int)(14 * MathF.Cos(angle)),
                Entity.Y + y - 1 + (int)(14 * MathF.Sin(angle))
            )) {
                Components = {
                    new BombProjectile {
                        velocity = new(2 * MathF.Cos(angle) + velocity.X * 0.5f, 2 * MathF.Sin(angle) + velocity.Y * 0.25f - 1),
                        Owner = Entity.EntityID,
                        Damage = 1,
                        Team = Team.Player
                    },
                },
            };

            Scene.Entities.Add(bomb);

            if(InputMapping.PrimaryFire.IsDown)
            {
                bulletDelay = baseBulletDelay + 3;

                Scene.OnEndOfFrame += () => {
                    if(bullet != null)
                    {
                        Scene?.Entities.Remove(bullet);
                        shootSound?.Stop();
                    }

                    bomb?.GetComponent<BombProjectile>().Explode(true);
                };
            }
        }
    }

    private void OnStateEnter(PlayerState state)
    {
        switch(state)
        {
            case PlayerState.IgnoreState:
                break;
            case PlayerState.StandIdle:
                if(OnGround)
                {
                    textureIndex = TextureIndex.Idle;
                    frame = 0;
                }
                else
                {
                    textureIndex = TextureIndex.Jump;
                }
                break;
            case PlayerState.Normal:
                break;
            case PlayerState.LedgeGrab:
                velocity = Vector2.Zero;
                if(Facing == 0) Facing = 1;
                break;
            case PlayerState.Dead:
                break;
            default:
                break;
        }
    }

    private void OnStateExit(PlayerState state)
    {
        switch(state)
        {
            case PlayerState.IgnoreState:
                break;
            case PlayerState.StandIdle:
                break;
            case PlayerState.Normal:
                break;
            case PlayerState.LedgeGrab:
                ledgegrabTimer = 15;
                break;
            case PlayerState.Dead:
                break;
        }
    }

    private void StateUpdate()
    {
        if(!_stateJustChanged)
        {
            CollidesWithJumpthroughs = true;
            CollidesWithSolids = true;
        }
        else
        {
            _stateJustChanged = false;
        }

        switch(State)
        {
            case PlayerState.StandIdle:
                velocity.X = MathUtil.Approach(velocity.X, 0, fric * 2);

                if(OnGround)
                {
                    textureIndex = TextureIndex.Idle;
                }

                break;
            case PlayerState.Normal: {
                canJump = true;
                canWalljump = true;

                if(duck > 0)
                    SetHitbox(MaskDuck, PivotDuck);
                else
                    SetHitbox(MaskNormal, PivotNormal);

                if(inputDir != 0)
                {
                    Facing = inputDir;

                    if(inputDir * velocity.X < 0)
                    {
                        if(Math.Abs(velocity.X) > moveSpeed * 0.6f)
                            skidding = true;
                        else
                            skidding = false;

                        velocity.X = MathUtil.Approach(velocity.X, 0, fric);
                    }
                    else if(OnGround && velocity.Y >= 0)
                    {
                        skidding = false;
                        if (duck == 0 && landTimer <= 0)
                        {
                            if(Math.Abs(velocity.X) > moveSpeed * 1.3f)
                                textureIndex = TextureIndex.RunFast;
                            else
                                textureIndex = TextureIndex.Run;
                        }
                        else if(duck > 0)
                        {
                            textureIndex = TextureIndex.Crawl;
                        }
                    }

                    if(inputDir * velocity.X < moveSpeed)
                    {
                        velocity.X = MathUtil.Approach(velocity.X, inputDir * moveSpeed, accel);
                    }

                    if(inputDir * velocity.X > moveSpeed && OnGround)
                    {
                        velocity.X = MathUtil.Approach(velocity.X, inputDir * moveSpeed, fric/3);
                    }

                    if(OnGround)
                    {
                        running = true;
                    }
                }
                else
                {
                    running = false;
                    velocity.X = MathUtil.Approach(velocity.X, oldVelocity.X, fric * 2);

                    if (Math.Abs(velocity.X) < moveSpeed)
                    {
                        skidding = false;
                        // run = MathUtil.Approach(run, 0, global.dt)
                    }
                    if (Math.Abs(velocity.X) < 1.5f && OnGround && landTimer <= 0)
                    {
                        bool lookingUp = InputMapping.Up.IsDown;
                        textureIndex = TextureIndex.Idle;
                        if(duck > 0)
                        {
                            textureIndex = TextureIndex.Duck;
                            frame = duck;
                            Lookup = -0.5f;
                        }
                        else if(lookingUp)
                        {
                            textureIndex = TextureIndex.LookUp;
                            Lookup = 1;
                        }
                        else
                        {
                            Lookup = 0;
                        }
                    }
                }

                if (InputMapping.Down.IsDown && OnGround)
                    duck = MathUtil.Approach(duck, 3, 1);
                else if(!CheckColliding(Hitbox.Shift(0, -6)))
                {
                    duck = MathUtil.Approach(duck, 0, 1);
                }

                if(!OnGround)
                {
                    Lookup = 0;

                    if(velocity.Y >= -1f)
                    {
                        if(CheckColliding(Hitbox.Shift(inputDir, 0), true))
                            wallslideTimer++;

                        CheckLedgeGrab();
                    }
                    else
                        wallslideTimer = 0;
                    if (wallslideTimer >= 5)
                        State = PlayerState.Wallslide;

                    jumpBuffer = MathUtil.Approach(jumpBuffer, 0, 1);

                    textureIndex = TextureIndex.Jump;
                    if (velocity.Y >= 0.1)
                        velocity.Y = MathUtil.Approach(velocity.Y, 20, gravity);
                    if (velocity.Y < 0)
                        velocity.Y = MathUtil.Approach(velocity.Y, 20, gravity);
                    else if (velocity.Y < 2)
                        velocity.Y = MathUtil.Approach(velocity.Y, 20, gravity * 0.25f);
                    if (velocity.Y < 0)
                        frame = MathUtil.Approach(frame, 1, 0.2f);
                    else if (velocity.Y >= 0.5)
                        frame = MathUtil.Approach(frame, 5, 0.5f);
                    else
                        frame = 3;
                }
                else
                {
                    if(onJumpthrough && InputMapping.Down.IsDown && !CheckColliding(BottomEdge.Shift(new(0, 2)), true))
                    {
                        Entity.Y += 2;

                        onJumpthrough = CheckCollidingJumpthrough(BottomEdge.Shift(0, 1));
                        if(onJumpthrough) OnGround = true;
                        else OnGround = CheckColliding(BottomEdge.Shift(0, 1));
                    }

                    wallslideTimer = 0;
                    oldVelocity = Vector2.Zero;
                    jumpBuffer = 5;
                }
                if (running && !CheckColliding(Hitbox.Shift(inputDir, 0), true))
                    frame += Math.Abs(velocity.X) / (8f / frameCounts[(int)textureIndex] * 6);
                else if (duck > 0)
                    frame += Math.Abs(velocity.X / 4);
                landTimer = MathUtil.Approach(landTimer, 0, 1);

                fxTrail = Math.Abs(velocity.X) > 1.3f * moveSpeed;

                break;
            }
            case PlayerState.Wallslide: {
                flipGun = true;
                canWalljump = true;

                if (velocity.Y < 0)
                    velocity.Y = MathUtil.Approach(velocity.Y, 20, 0.5f);
                else
                    velocity.Y = MathUtil.Approach(velocity.Y, 20 / 3f, gravity / 3f);
                if (!CheckColliding(Hitbox.Shift(inputDir * 2, 0), true))
                {
                    State = PlayerState.Normal;
                    wallslideTimer = 0;
                }
                else
                {
                    if(CheckLedgeGrab()) break;
                }
                textureIndex = TextureIndex.Wallslide;
                // var n = choose(0, 1, 0, 1, 1, 0, 0, 0);
                // if(n == 1)
                //     with (instance_create_depth(x + 4 * sign(facing), random_range(bbox_bottom - 12, bbox_bottom), depth - 1, fx_dust))
                //     {
                //         vz = 0
                //         if(instance_exists(other.platformtarget))
                //             vx += other.platformtarget.hsp
                //         sprite_index = spr_fx_dust2
                //     }
                if (inputDir == 0 || OnGround)
                {
                    State = PlayerState.Normal;
                    wallslideTimer = 0;
                }
                if (Math.Sign(inputDir) == -Math.Sign(Facing))
                {
                    State = PlayerState.Normal;
                    wallslideTimer = 0;
                    Facing = Math.Sign(inputDir);
                }
                velocity.Y = MathHelper.Clamp(velocity.Y, -99, 2);
                break;
            }

            case PlayerState.LedgeGrab:
            {
                flipGun = true;
                canLedgeGrab = false;
                canWalljump = false;
                duck = 0;
                canJump = true;
                velocity = Vector2.Zero;

                textureIndex = TextureIndex.LedgeGrab;

                if((platformTarget == null || !platformTarget.Intersects(Hitbox.Shift(Facing, 0))) && !CheckColliding(Hitbox.Shift(Facing, 0), true))
                {
                    textureIndex = TextureIndex.Jump;
                    SetHitbox(MaskNormal, PivotNormal);
                    MoveY(12, null);
                    MoveX(-4 * Facing, null);
                    State = PlayerState.Normal;
                    break;
                }

                // ledgeClimb check

                break;
            }

            case PlayerState.IgnoreState: default:
                break;
        }
    }

    private bool CheckLedgeGrab()
    {
        Solid _s = null;
        Rectangle _w = Rectangle.Empty;

        if(Scene.CollisionSystem.SolidPlace(Hitbox.Shift(inputDir, 0)) is Solid solid)
        {
            _w = solid.Hitbox;
            _s = solid;
        }
        else if(Scene.CollisionSystem.GetTile(new Point(Left + inputDir + (inputDir > 0 ? Width : 0), Top).Divide(CollisionSystem.TileSize)) != 0)
        {
            Point point = new(Facing < 0 ? Left - 1 : Right + 1, Top);
            Point tilePos = MathUtil.Snap(point.ToVector2(), CollisionSystem.TileSize).ToPoint();
            _w = new(tilePos, new(CollisionSystem.TileSize));
        }

        if(_w != Rectangle.Empty)
        {
            if (canLedgeGrab && ledgegrabTimer == 0 && !CheckColliding(Hitbox))
            {
                if(!CheckColliding(new((inputDir == 1) ? _w.Left + 1 : _w.Right - 1, _w.Top - 1, 1, 1), true)
                && !CheckColliding(new((inputDir == 1) ? _w.Left - 2 : _w.Right + 2, _w.Top + 18, 1, 1), true))
                {
                    if (Math.Sign(Top - _w.Top) <= 0 && !CheckColliding(new(Left, _w.Top - 1, Width, Height), true) && !CheckColliding(Hitbox.Shift(0, 2), true))
                    {
                        wallslideTimer = 0;
                        State = PlayerState.LedgeGrab;

                        SetHitbox(MaskLedge, PivotLedge);
                        textureIndex = TextureIndex.LedgeGrab;

                        Entity.Y = _w.Top - bboxOffset.Y;
                        Entity.X = ((inputDir == 1) ? _w.Left - Width : _w.Right) - bboxOffset.X;
                        Facing = Math.Sign(_w.Left - Left);

                        platformTarget = _s;

                        return true;
                    }
                }
            }
        }

        return false;
    }

    public override bool IsRiding(Solid solid)
    {
        return base.IsRiding(solid) || ((State == PlayerState.LedgeGrab || State == PlayerState.Wallslide) && solid.Intersects(Hitbox.Shift(Facing, 0)));
    }

    private void Grounded()
    {
        jumpCancelled = false;
    }

    private void RecalculateStats()
    {
        moveSpeed = baseMoveSpeed;
        jumpSpeed = baseJumpSpeed;

        canJump = true;
        canLedgeGrab = true;
        canWalljump = true;
        flipGun = false;

        accel = baseGroundAcceleration;
        fric = baseGroundFriction;
        if(!OnGround)
        {
            accel = baseAirAcceleration;
            fric = baseAirFriction;
            if(Math.Abs(velocity.X) > moveSpeed * 1.3)
                fric *= 0.5f;
        }
    }

    public override void Draw()
    {
        foreach(var image in afterImages)
        {
            if(!Visible) continue;

            var texture = textures[(int)image.TextureIndex];
            Rectangle drawFrame = GraphicsUtil.GetFrameInStrip(texture, image.Frame, frameCounts[(int)image.TextureIndex]);

            if(JellyBackend.DebugEnabled)
            {
                // Renderer.SpriteBatch.DrawNineSlice(
                //     ContentLoader.LoadTexture("Images/Debug/tileOutline"),
                //     new Rectangle((int)image.Position.X - Width / 2, (int)image.Position.Y - Height, Width, Height),
                //     null,
                //     new Point(1),
                //     new Point(1),
                //     Color.Blue * 0.75f,
                //     Vector2.Zero,
                //     SpriteEffects.None,
                //     0
                // );
            }

            Renderer.SpriteBatch.Draw(
                texture,
                image.Position,
                drawFrame,
                image.Color * (image.Alpha * 0.5f),
                image.Rotation,
                image.Pivot,
                image.Scale,
                image.SpriteEffects,
                0
            );
        }

        SpriteEffects spriteEffects = VisualFacing < 0 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

        while(frame > frameCounts[(int)textureIndex])
            frame -= frameCounts[(int)textureIndex];

        {
            var texture = textures[(int)textureIndex];
            Rectangle drawFrame = GraphicsUtil.GetFrameInStrip(texture, frame, frameCounts[(int)textureIndex]);

            Renderer.SpriteBatch.Draw(
                texture,
                Entity.Position.ToVector2(), drawFrame,
                Color.White,
                0, Entity.Pivot.ToVector2(),
                new Vector2(stretch, squash),
                spriteEffects,
                0
            );
        }

        {
            var texture = ContentLoader.LoadTexture("Images/Player/gun");

            int x = gunOffset.X;
            int y = gunOffset.Y;

            var gunSpriteEffects = (SpriteEffects)((((int)spriteEffects) << 1) & 2) ^ (flipGun ? SpriteEffects.FlipVertically : 0);

            var angle = MathHelper.ToRadians(MathF.Round(MathHelper.ToDegrees(gunAngle) / 10) * 10);

            Renderer.SpriteBatch.Draw(
                texture,
                new Vector2(
                    Entity.X + x * stretch * VisualFacing + -recoil * MathF.Cos(angle),
                    Entity.Y + y * squash + -recoil * MathF.Sin(angle)
                ),
                null,
                Color.White,
                angle,
                new Vector2(2, 8),
                new Vector2(stretch, squash),
                gunSpriteEffects,
                0
            );
        }

        if(JellyBackend.DebugEnabled)
        {
            if(platformTarget != null)
            {
                Renderer.SpriteBatch.DrawNineSlice(
                    ContentLoader.LoadTexture("Images/Debug/tileOutline"),
                    platformTarget.Hitbox,
                    null,
                    new Point(1),
                    new Point(1),
                    Color.Blue
                );
            }

            Renderer.SpriteBatch.DrawNineSlice(ContentLoader.LoadTexture("Images/Debug/tileOutline"), Hitbox, null, new Point(1), new Point(1), Color.Red * 0.5f);

            Renderer.SpriteBatch.DrawNineSlice(
                ContentLoader.LoadTexture("Images/Debug/tileOutline"),
                new(Entity.Position - new Point(2, 2), new Point(4, 4)),
                null,
                new Point(1),
                new Point(1),
                Color.Blue
            );
        }
    }

    public override void DrawUI()
    {
        if(!JellyBackend.DebugEnabled) return;

        Renderer.SpriteBatch.DrawStringSpacesFix(Fonts.RegularFont, State.ToString(), new Vector2(1, 1), Color.White, 6);
    }
}

public class PlayerInputMapping
{
    public MappedInput Right { get; set; } = new MappedInput.Keyboard(Keys.D);
    public MappedInput Left { get; set; } = new MappedInput.Keyboard(Keys.A);
    public MappedInput Down { get; set; } = new MappedInput.Keyboard(Keys.S);
    public MappedInput Up { get; set; } = new MappedInput.Keyboard(Keys.W);
    public MappedInput Jump { get; set; } = new MappedInput.Keyboard(Keys.Space);
    public MappedInput PrimaryFire { get; set; } = new MappedInput.Mouse(MouseButtons.LeftButton);
    public MappedInput SecondaryFire { get; set; } = new MappedInput.Mouse(MouseButtons.RightButton);
}
