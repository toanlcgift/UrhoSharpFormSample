using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Urho;
using Urho.Actions;
using Urho.Gui;
using Urho.Physics;
using Urho.Resources;
using Urho.Shapes;

namespace FormsSample
{
    public class Character : Component
    {
        /// Movement controls. Assigned by the main program each frame.
        public Controls Controls { get; set; } = new Controls();

        /// Grounded flag for movement.
        bool onGround;
        /// Jump flag.
        bool okToJump;
        /// In air timer. Due to possible physics inaccuracy, character can be off ground for max. 1/10 second and still be allowed to move.
        float inAirTimer;

        RigidBody body;
        AnimationController animCtrl;

        public Character()
        {
            okToJump = true;
        }

        // constructor needed for deserialization
        public Character(IntPtr handle) : base(handle) { }

        public override void OnAttachedToNode(Node node)
        {
            // Component has been inserted into its scene node. Subscribe to events now
            node.SubscribeToNodeCollision(HandleNodeCollision);
        }

        public void FixedUpdate(float timeStep)
        {
            animCtrl = animCtrl ?? GetComponent<AnimationController>();
            body = body ?? GetComponent<RigidBody>();

            // Update the in air timer. Reset if grounded
            if (!onGround)
                inAirTimer += timeStep;
            else
                inAirTimer = 0.0f;
            // When character has been in air less than 1/10 second, it's still interpreted as being on ground
            bool softGrounded = inAirTimer < CharacterDemo.InairThresholdTime;

            // Update movement & animation
            var rot = Node.Rotation;
            Vector3 moveDir = Vector3.Zero;
            var velocity = body.LinearVelocity;
            // Velocity on the XZ plane
            Vector3 planeVelocity = new Vector3(velocity.X, 0.0f, velocity.Z);

            if (Controls.IsDown(CharacterDemo.CtrlForward))
                moveDir += Vector3.UnitZ;
            if (Controls.IsDown(CharacterDemo.CtrlBack))
                moveDir += new Vector3(0f, 0f, -1f);
            if (Controls.IsDown(CharacterDemo.CtrlLeft))
                moveDir += new Vector3(-1f, 0f, 0f);
            if (Controls.IsDown(CharacterDemo.CtrlRight))
                moveDir += Vector3.UnitX;

            // Normalize move vector so that diagonal strafing is not faster
            if (moveDir.LengthSquared > 0.0f)
                moveDir.Normalize();

            // If in air, allow control, but slower than when on ground
            body.ApplyImpulse(rot * moveDir * (softGrounded ? CharacterDemo.MoveForce : CharacterDemo.InairMoveForce));

            if (softGrounded)
            {
                // When on ground, apply a braking force to limit maximum ground velocity
                Vector3 brakeForce = -planeVelocity * CharacterDemo.BrakeForce;
                body.ApplyImpulse(brakeForce);

                // Jump. Must release jump control inbetween jumps
                if (Controls.IsDown(CharacterDemo.CtrlJump))
                {
                    if (okToJump)
                    {
                        body.ApplyImpulse(Vector3.UnitY * CharacterDemo.JumpForce);
                        okToJump = false;
                    }
                }
                else
                    okToJump = true;
            }

            // Play walk animation if moving on ground, otherwise fade it out
            if (softGrounded && !moveDir.Equals(Vector3.Zero))
                animCtrl.PlayExclusive("Models/Jack_Walk.ani", 0, true, 0.2f);
            else
                animCtrl.Stop("Models/Jack_Walk.ani", 0.2f);
            // Set walk animation speed proportional to velocity
            animCtrl.SetSpeed("Models/Jack_Walk.ani", planeVelocity.Length * 0.3f);

            // Reset grounded flag for next frame
            onGround = false;
        }

        void HandleNodeCollision(NodeCollisionEventArgs args)
        {
            foreach (var contact in args.Contacts)
            {
                // If contact is below node center and mostly vertical, assume it's a ground contact
                if (contact.ContactPosition.Y < (Node.Position.Y + 1.0f))
                {
                    float level = Math.Abs(contact.ContactNormal.Y);
                    if (level > 0.75)
                        onGround = true;
                }
            }
        }
    }

    public class Touch : Component
    {
        readonly float touchSensitivity;
        readonly Input input;
        bool zoom;

        public float CameraDistance { get; set; }
        public bool UseGyroscope { get; set; }

        public Touch(IntPtr handle) : base(handle) { }

        public Touch(float touchSensitivity, Input input)
        {
            this.touchSensitivity = touchSensitivity;
            this.input = input;
            CameraDistance = CharacterDemo.CameraInitialDist;
            zoom = false;
            UseGyroscope = false;
        }

        public void UpdateTouches(Controls controls)
        {
            zoom = false; // reset bool

            // Zoom in/out
            if (input.NumTouches == 2)
            {
                TouchState touch1, touch2;
                touch1 = input.GetTouch(0);
                touch2 = input.GetTouch(1);

                // Check for zoom pattern (touches moving in opposite directions and on empty space)
                if (touch1.TouchedElement != null && touch2.TouchedElement != null && ((touch1.Delta.Y > 0 && touch2.Delta.Y < 0) || (touch1.Delta.Y < 0 && touch2.Delta.Y > 0)))
                    zoom = true;
                else
                    zoom = false;

                if (zoom)
                {
                    int sens = 0;
                    // Check for zoom direction (in/out)
                    if (Math.Abs(touch1.Position.Y - touch2.Position.Y) > Math.Abs(touch1.LastPosition.Y - touch2.LastPosition.Y))
                        sens = -1;
                    else
                        sens = 1;
                    CameraDistance += Math.Abs(touch1.Delta.Y - touch2.Delta.Y) * sens * touchSensitivity / 50.0f;
                    CameraDistance = MathHelper.Clamp(CameraDistance, CharacterDemo.CameraMinDist, CharacterDemo.CameraMaxDist); // Restrict zoom range to [1;20]
                }
            }

            // Gyroscope (emulated by SDL through a virtual joystick)
            if (UseGyroscope && input.NumJoysticks > 0)  // numJoysticks = 1 on iOS & Android
            {
                JoystickState joystick;
                if (input.TryGetJoystickState(0, out joystick) && joystick.Axes.Size >= 2)
                {
                    if (joystick.GetAxisPosition(0) < -CharacterDemo.GyroscopeThreshold)
                        controls.Set(CharacterDemo.CtrlLeft, true);
                    if (joystick.GetAxisPosition(0) > CharacterDemo.GyroscopeThreshold)
                        controls.Set(CharacterDemo.CtrlRight, true);
                    if (joystick.GetAxisPosition(1) < -CharacterDemo.GyroscopeThreshold)
                        controls.Set(CharacterDemo.CtrlForward, true);
                    if (joystick.GetAxisPosition(1) > CharacterDemo.GyroscopeThreshold)
                        controls.Set(CharacterDemo.CtrlBack, true);
                }
            }
        }
    }
    public class CharacterDemo : Sample
    {
        Scene scene;

        public const float CameraMinDist = 1.0f;
        public const float CameraInitialDist = 5.0f;
        public const float CameraMaxDist = 20.0f;

        public const float GyroscopeThreshold = 0.1f;

        public const int CtrlForward = 1;
        public const int CtrlBack = 2;
        public const int CtrlLeft = 4;
        public const int CtrlRight = 8;
        public const int CtrlJump = 16;

        public const float MoveForce = 0.8f;
        public const float InairMoveForce = 0.02f;
        public const float BrakeForce = 0.2f;
        public const float JumpForce = 7.0f;
        public const float YawSensitivity = 0.1f;
        public const float InairThresholdTime = 0.1f;

        /// Touch utility object.
        Touch touch;
        /// The controllable character component.
        Character character;
        /// First person camera flag.
        bool firstPerson;
        PhysicsWorld physicsWorld;

        public CharacterDemo(ApplicationOptions options = null) : base(options) { }

        protected override void Start()
        {
            base.Start();
            if (TouchEnabled)
                touch = new Touch(TouchSensitivity, Input);
            CreateScene();
            CreateCharacter();
            SimpleCreateInstructionsWithWasd("\nSpace to jump, F to toggle 1st/3rd person\nF5 to save scene, F7 to load");
            SubscribeToEvents();
        }

        void SubscribeToEvents()
        {
            Engine.SubscribeToPostUpdate(HandlePostUpdate);
            physicsWorld.SubscribeToPhysicsPreStep(HandlePhysicsPreStep);
        }

        void HandlePhysicsPreStep(PhysicsPreStepEventArgs args)
        {
            character?.FixedUpdate(args.TimeStep);
        }

        void HandlePostUpdate(PostUpdateEventArgs args)
        {
            if (character == null)
                return;

            Node characterNode = character.Node;

            // Get camera lookat dir from character yaw + pitch
            Quaternion rot = characterNode.Rotation;
            Quaternion dir = rot * Quaternion.FromAxisAngle(Vector3.UnitX, character.Controls.Pitch);

            // Turn head to camera pitch, but limit to avoid unnatural animation
            Node headNode = characterNode.GetChild("Bip01_Head", true);
            float limitPitch = MathHelper.Clamp(character.Controls.Pitch, -45.0f, 45.0f);
            Quaternion headDir = rot * Quaternion.FromAxisAngle(new Vector3(1.0f, 0.0f, 0.0f), limitPitch);
            // This could be expanded to look at an arbitrary target, now just look at a point in front
            Vector3 headWorldTarget = headNode.WorldPosition + headDir * new Vector3(0.0f, 0.0f, 1.0f);
            headNode.LookAt(headWorldTarget, new Vector3(0.0f, 1.0f, 0.0f), TransformSpace.World);
            // Correct head orientation because LookAt assumes Z = forward, but the bone has been authored differently (Y = forward)
            headNode.Rotate(new Quaternion(0.0f, 90.0f, 90.0f), TransformSpace.Local);

            if (firstPerson)
            {
                CameraNode.Position = headNode.WorldPosition + rot * new Vector3(0.0f, 0.15f, 0.2f);
                CameraNode.Rotation = dir;
            }
            else
            {
                // Third person camera: position behind the character
                Vector3 aimPoint = characterNode.Position + rot * new Vector3(0.0f, 1.7f, 0.0f);

                // Collide camera ray with static physics objects (layer bitmask 2) to ensure we see the character properly
                Vector3 rayDir = dir * new Vector3(0f, 0f, -1f);
                float rayDistance = touch != null ? touch.CameraDistance : CameraInitialDist;

                PhysicsRaycastResult result = new PhysicsRaycastResult();
                scene.GetComponent<PhysicsWorld>().RaycastSingle(ref result, new Ray(aimPoint, rayDir), rayDistance, 2);
                if (result.Body != null)
                    rayDistance = Math.Min(rayDistance, result.Distance);
                rayDistance = MathHelper.Clamp(rayDistance, CameraMinDist, CameraMaxDist);

                CameraNode.Position = aimPoint + rayDir * rayDistance;
                CameraNode.Rotation = dir;
            }
        }

        protected override void OnUpdate(float timeStep)
        {
            Input input = Input;

            if (character != null)
            {
                // Clear previous controls
                character.Controls.Set(CtrlForward | CtrlBack | CtrlLeft | CtrlRight | CtrlJump, false);

                // Update controls using touch utility class
                touch?.UpdateTouches(character.Controls);

                // Update controls using keys
                if (UI.FocusElement == null)
                {
                    if (touch == null || !touch.UseGyroscope)
                    {
                        character.Controls.Set(CtrlForward, input.GetKeyDown(Key.W));
                        character.Controls.Set(CtrlBack, input.GetKeyDown(Key.S));
                        character.Controls.Set(CtrlLeft, input.GetKeyDown(Key.A));
                        character.Controls.Set(CtrlRight, input.GetKeyDown(Key.D));
                    }
                    character.Controls.Set(CtrlJump, input.GetKeyDown(Key.Space));

                    // Add character yaw & pitch from the mouse motion or touch input
                    if (TouchEnabled)
                    {
                        for (uint i = 0; i < input.NumTouches; ++i)
                        {
                            TouchState state = input.GetTouch(i);
                            if (state.TouchedElement == null)    // Touch on empty space
                            {
                                Camera camera = CameraNode.GetComponent<Camera>();
                                if (camera == null)
                                    return;

                                var graphics = Graphics;
                                character.Controls.Yaw += TouchSensitivity * camera.Fov / graphics.Height * state.Delta.X;
                                character.Controls.Pitch += TouchSensitivity * camera.Fov / graphics.Height * state.Delta.Y;
                            }
                        }
                    }
                    else
                    {
                        character.Controls.Yaw += (float)input.MouseMove.X * YawSensitivity;
                        character.Controls.Pitch += (float)input.MouseMove.Y * YawSensitivity;
                    }
                    // Limit pitch
                    character.Controls.Pitch = MathHelper.Clamp(character.Controls.Pitch, -80.0f, 80.0f);

                    // Switch between 1st and 3rd person
                    if (input.GetKeyPress(Key.F))
                        firstPerson = !firstPerson;

                    // Turn on/off gyroscope on mobile platform
                    if (touch != null && input.GetKeyPress(Key.G))
                        touch.UseGyroscope = !touch.UseGyroscope;

                    if (input.GetKeyPress(Key.F5))
                    {
                        scene.SaveXml(FileSystem.ProgramDir + "Data/Scenes/CharacterDemo.xml", "\t");
                    }
                    if (input.GetKeyPress(Key.F7))
                    {
                        scene.LoadXml(FileSystem.ProgramDir + "Data/Scenes/CharacterDemo.xml");
                        Node characterNode = scene.GetChild("Jack", true);
                        if (characterNode != null)
                        {
                            character = characterNode.GetComponent<Character>();
                        }
                        physicsWorld = scene.CreateComponent<PhysicsWorld>();
                        physicsWorld.SubscribeToPhysicsPreStep(HandlePhysicsPreStep);
                    }
                }

                // Set rotation already here so that it's updated every rendering frame instead of every physics frame
                if (character != null)
                    character.Node.Rotation = Quaternion.FromAxisAngle(Vector3.UnitY, character.Controls.Yaw);
            }
        }

        void CreateScene()
        {
            var cache = ResourceCache;

            scene = new Scene();

            // Create scene subsystem components
            scene.CreateComponent<Octree>();
            physicsWorld = scene.CreateComponent<PhysicsWorld>();

            // Create camera and define viewport. We will be doing load / save, so it's convenient to create the camera outside the scene,
            // so that it won't be destroyed and recreated, and we don't have to redefine the viewport on load
            CameraNode = new Node();
            Camera camera = CameraNode.CreateComponent<Camera>();
            camera.FarClip = 300.0f;
            Renderer.SetViewport(0, new Viewport(Context, scene, camera, null));

            // Create static scene content. First create a zone for ambient lighting and fog control
            Node zoneNode = scene.CreateChild("Zone");
            Zone zone = zoneNode.CreateComponent<Zone>();
            zone.AmbientColor = new Color(0.15f, 0.15f, 0.15f);
            zone.FogColor = new Color(0.5f, 0.5f, 0.7f);
            zone.FogStart = 100.0f;
            zone.FogEnd = 300.0f;
            zone.SetBoundingBox(new BoundingBox(-1000.0f, 1000.0f));

            // Create a directional light with cascaded shadow mapping
            Node lightNode = scene.CreateChild("DirectionalLight");
            lightNode.SetDirection(new Vector3(0.3f, -0.5f, 0.425f));
            Light light = lightNode.CreateComponent<Light>();
            light.LightType = LightType.Directional;
            light.CastShadows = true;
            light.ShadowBias = new BiasParameters(0.00025f, 0.5f);
            light.ShadowCascade = new CascadeParameters(10.0f, 50.0f, 200.0f, 0.0f, 0.8f);
            light.SpecularIntensity = 0.5f;

            // Create the floor object
            Node floorNode = scene.CreateChild("Floor");
            floorNode.Position = new Vector3(0.0f, -0.5f, 0.0f);
            floorNode.Scale = new Vector3(200.0f, 1.0f, 200.0f);
            StaticModel sm = floorNode.CreateComponent<StaticModel>();
            sm.Model = cache.GetModel("Models/Box.mdl");
            sm.SetMaterial(cache.GetMaterial("Materials/Stone.xml"));

            RigidBody body = floorNode.CreateComponent<RigidBody>();
            // Use collision layer bit 2 to mark world scenery. This is what we will raycast against to prevent camera from going
            // inside geometry
            body.CollisionLayer = 2;
            CollisionShape shape = floorNode.CreateComponent<CollisionShape>();
            shape.SetBox(Vector3.One, Vector3.Zero, Quaternion.Identity);

            // Create mushrooms of varying sizes
            const uint numMushrooms = 60;
            for (uint i = 0; i < numMushrooms; ++i)
            {
                Node objectNode = scene.CreateChild("Mushroom");
                objectNode.Position = new Vector3(NextRandom(180.0f) - 90.0f, 0.0f, NextRandom(180.0f) - 90.0f);
                objectNode.Rotation = new Quaternion(0.0f, NextRandom(360.0f), 0.0f);
                objectNode.SetScale(2.0f + NextRandom(5.0f));
                StaticModel o = objectNode.CreateComponent<StaticModel>();
                o.Model = cache.GetModel("Models/Mushroom.mdl");
                o.SetMaterial(cache.GetMaterial("Materials/Mushroom.xml"));
                o.CastShadows = true;

                body = objectNode.CreateComponent<RigidBody>();
                body.CollisionLayer = 2;
                shape = objectNode.CreateComponent<CollisionShape>();
                shape.SetTriangleMesh(o.Model, 0, Vector3.One, Vector3.Zero, Quaternion.Identity);
            }

            // Create movable boxes. Let them fall from the sky at first
            const uint numBoxes = 100;
            for (uint i = 0; i < numBoxes; ++i)
            {
                float scale = NextRandom(2.0f) + 0.5f;

                Node objectNode = scene.CreateChild("Box");
                objectNode.Position = new Vector3(NextRandom(180.0f) - 90.0f, NextRandom(10.0f) + 10.0f, NextRandom(180.0f) - 90.0f);
                objectNode.Rotation = new Quaternion(NextRandom(360.0f), NextRandom(360.0f), NextRandom(360.0f));
                objectNode.SetScale(scale);
                StaticModel o = objectNode.CreateComponent<StaticModel>();
                o.Model = cache.GetModel("Models/Box.mdl");
                o.SetMaterial(cache.GetMaterial("Materials/Stone.xml"));
                o.CastShadows = true;

                body = objectNode.CreateComponent<RigidBody>();
                body.CollisionLayer = 2;
                // Bigger boxes will be heavier and harder to move
                body.Mass = scale * 2.0f;
                shape = objectNode.CreateComponent<CollisionShape>();
                shape.SetBox(Vector3.One, Vector3.Zero, Quaternion.Identity);
            }
        }

        void CreateCharacter()
        {
            var cache = ResourceCache;

            Node objectNode = scene.CreateChild("Jack");
            objectNode.Position = (new Vector3(0.0f, 1.0f, 0.0f));

            // Create the rendering component + animation controller
            AnimatedModel obj = objectNode.CreateComponent<AnimatedModel>();
            obj.Model = cache.GetModel("Models/Jack.mdl");
            obj.SetMaterial(cache.GetMaterial("Materials/Jack.xml"));
            obj.CastShadows = true;
            objectNode.CreateComponent<AnimationController>();

            // Set the head bone for manual control
            //obj.Skeleton.GetBoneSafe("Bip01_Head").Animated = false;

            // Create rigidbody, and set non-zero mass so that the body becomes dynamic
            RigidBody body = objectNode.CreateComponent<RigidBody>();
            body.CollisionLayer = 1;
            body.Mass = 1.0f;

            // Set zero angular factor so that physics doesn't turn the character on its own.
            // Instead we will control the character yaw manually
            body.SetAngularFactor(Vector3.Zero);

            // Set the rigidbody to signal collision also when in rest, so that we get ground collisions properly
            body.CollisionEventMode = CollisionEventMode.Always;

            // Set a capsule shape for collision
            CollisionShape shape = objectNode.CreateComponent<CollisionShape>();
            shape.SetCapsule(0.7f, 1.8f, new Vector3(0.0f, 0.9f, 0.0f), Quaternion.Identity);

            // Create the character logic component, which takes care of steering the rigidbody
            // Remember it so that we can set the controls. Use a WeakPtr because the scene hierarchy already owns it
            // and keeps it alive as long as it's not removed from the hierarchy
            character = new Character();
            objectNode.AddComponent(character);
        }

        /// <summary>
        /// Set custom Joystick layout for mobile platforms
        /// </summary>
        protected override string JoystickLayoutPatch =>
            "<patch>" +
            "    <remove sel=\"/element/element[./attribute[@name='Name' and @value='Button0']]/attribute[@name='Is Visible']\" />" +
            "    <replace sel=\"/element/element[./attribute[@name='Name' and @value='Button0']]/element[./attribute[@name='Name' and @value='Label']]/attribute[@name='Text']/@value\">1st/3rd</replace>" +
            "    <add sel=\"/element/element[./attribute[@name='Name' and @value='Button0']]\">" +
            "        <element type=\"Text\">" +
            "            <attribute name=\"Name\" value=\"KeyBinding\" />" +
            "            <attribute name=\"Text\" value=\"F\" />" +
            "        </element>" +
            "    </add>" +
            "    <remove sel=\"/element/element[./attribute[@name='Name' and @value='Button1']]/attribute[@name='Is Visible']\" />" +
            "    <replace sel=\"/element/element[./attribute[@name='Name' and @value='Button1']]/element[./attribute[@name='Name' and @value='Label']]/attribute[@name='Text']/@value\">Jump</replace>" +
            "    <add sel=\"/element/element[./attribute[@name='Name' and @value='Button1']]\">" +
            "        <element type=\"Text\">" +
            "            <attribute name=\"Name\" value=\"KeyBinding\" />" +
            "            <attribute name=\"Text\" value=\"SPACE\" />" +
            "        </element>" +
            "    </add>" +
            "</patch>";
    }

    public class Sample : Application
    {
        UrhoConsole console;
        DebugHud debugHud;
        ResourceCache cache;
        Sprite logoSprite;
        UI ui;

        protected const float TouchSensitivity = 2;
        protected float Yaw { get; set; }
        protected float Pitch { get; set; }
        protected bool TouchEnabled { get; set; }
        protected Node CameraNode { get; set; }
        protected MonoDebugHud MonoDebugHud { get; set; }

        [Preserve]
        protected Sample(ApplicationOptions options = null) : base(options) { }

        static Sample()
        {
            Urho.Application.UnhandledException += Application_UnhandledException1;
        }

        static void Application_UnhandledException1(object sender, UnhandledExceptionEventArgs e)
        {
            if (Debugger.IsAttached && !e.Exception.Message.Contains("BlueHighway.ttf"))
                Debugger.Break();
            e.Handled = true;
        }

        protected bool IsLogoVisible
        {
            get { return logoSprite.Visible; }
            set { logoSprite.Visible = value; }
        }

        static readonly Random random = new Random();
        /// Return a random float between 0.0 (inclusive) and 1.0 (exclusive.)
        public static float NextRandom() { return (float)random.NextDouble(); }
        /// Return a random float between 0.0 and range, inclusive from both ends.
        public static float NextRandom(float range) { return (float)random.NextDouble() * range; }
        /// Return a random float between min and max, inclusive from both ends.
        public static float NextRandom(float min, float max) { return (float)((random.NextDouble() * (max - min)) + min); }
        /// Return a random integer between min and max - 1.
        public static int NextRandom(int min, int max) { return random.Next(min, max); }

        /// <summary>
        /// Joystick XML layout for mobile platforms
        /// </summary>
        protected virtual string JoystickLayoutPatch => string.Empty;

        protected override void Start()
        {
            Log.LogMessage += e => Debug.WriteLine($"[{e.Level}] {e.Message}");
            base.Start();
            if (Platform == Platforms.Android ||
                Platform == Platforms.iOS ||
                Options.TouchEmulation)
            {
                InitTouchInput();
            }
            Input.Enabled = true;
            MonoDebugHud = new MonoDebugHud(this);
            MonoDebugHud.Show();

            CreateLogo();
            SetWindowAndTitleIcon();
            CreateConsoleAndDebugHud();
            Input.SubscribeToKeyDown(HandleKeyDown);
        }

        protected override void OnUpdate(float timeStep)
        {
            MoveCameraByTouches(timeStep);
            base.OnUpdate(timeStep);
        }

        /// <summary>
        /// Move camera for 2D samples
        /// </summary>
        protected void SimpleMoveCamera2D(float timeStep)
        {
            // Do not move if the UI has a focused element (the console)
            if (UI.FocusElement != null)
                return;

            // Movement speed as world units per second
            const float moveSpeed = 4.0f;

            // Read WASD keys and move the camera scene node to the corresponding direction if they are pressed
            if (Input.GetKeyDown(Key.W)) CameraNode.Translate(Vector3.UnitY * moveSpeed * timeStep);
            if (Input.GetKeyDown(Key.S)) CameraNode.Translate(-Vector3.UnitY * moveSpeed * timeStep);
            if (Input.GetKeyDown(Key.A)) CameraNode.Translate(-Vector3.UnitX * moveSpeed * timeStep);
            if (Input.GetKeyDown(Key.D)) CameraNode.Translate(Vector3.UnitX * moveSpeed * timeStep);

            if (Input.GetKeyDown(Key.PageUp))
            {
                Camera camera = CameraNode.GetComponent<Camera>();
                camera.Zoom = camera.Zoom * 1.01f;
            }

            if (Input.GetKeyDown(Key.PageDown))
            {
                Camera camera = CameraNode.GetComponent<Camera>();
                camera.Zoom = camera.Zoom * 0.99f;
            }
        }

        /// <summary>
        /// Move camera for 3D samples
        /// </summary>
        protected void SimpleMoveCamera3D(float timeStep, float moveSpeed = 10.0f)
        {
            const float mouseSensitivity = .1f;

            if (UI.FocusElement != null)
                return;

            var mouseMove = Input.MouseMove;
            Yaw += mouseSensitivity * mouseMove.X;
            Pitch += mouseSensitivity * mouseMove.Y;
            Pitch = MathHelper.Clamp(Pitch, -90, 90);

            CameraNode.Rotation = new Quaternion(Pitch, Yaw, 0);

            if (Input.GetKeyDown(Key.W)) CameraNode.Translate(Vector3.UnitZ * moveSpeed * timeStep);
            if (Input.GetKeyDown(Key.S)) CameraNode.Translate(-Vector3.UnitZ * moveSpeed * timeStep);
            if (Input.GetKeyDown(Key.A)) CameraNode.Translate(-Vector3.UnitX * moveSpeed * timeStep);
            if (Input.GetKeyDown(Key.D)) CameraNode.Translate(Vector3.UnitX * moveSpeed * timeStep);
        }

        protected void MoveCameraByTouches(float timeStep)
        {
            if (!TouchEnabled || CameraNode == null)
                return;

            var input = Input;
            for (uint i = 0, num = input.NumTouches; i < num; ++i)
            {
                TouchState state = input.GetTouch(i);
                if (state.TouchedElement != null)
                    continue;

                if (state.Delta.X != 0 || state.Delta.Y != 0)
                {
                    var camera = CameraNode.GetComponent<Camera>();
                    if (camera == null)
                        return;

                    var graphics = Graphics;
                    Yaw += TouchSensitivity * camera.Fov / graphics.Height * state.Delta.X;
                    Pitch += TouchSensitivity * camera.Fov / graphics.Height * state.Delta.Y;
                    CameraNode.Rotation = new Quaternion(Pitch, Yaw, 0);
                }
                else
                {
                    var cursor = UI.Cursor;
                    if (cursor != null && cursor.Visible)
                        cursor.Position = state.Position;
                }
            }
        }

        protected void SimpleCreateInstructionsWithWasd(string extra = "")
        {
            SimpleCreateInstructions("Use WASD keys and mouse/touch to move" + extra);
        }

        protected void SimpleCreateInstructions(string text = "")
        {
            var textElement = new Text()
            {
                Value = text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            textElement.SetFont(ResourceCache.GetFont("Fonts/Anonymous Pro.ttf"), 15);
            UI.Root.AddChild(textElement);
        }

        void CreateLogo()
        {
            cache = ResourceCache;
            var logoTexture = cache.GetTexture2D("Textures/LogoLarge.png");

            if (logoTexture == null)
                return;

            ui = UI;
            logoSprite = ui.Root.CreateSprite();
            logoSprite.Texture = logoTexture;
            int w = logoTexture.Width;
            int h = logoTexture.Height;
            logoSprite.SetScale(256.0f / w);
            logoSprite.SetSize(w, h);
            logoSprite.SetHotSpot(0, h);
            logoSprite.SetAlignment(HorizontalAlignment.Left, VerticalAlignment.Bottom);
            logoSprite.Opacity = 0.75f;
            logoSprite.Priority = -100;
        }

        void SetWindowAndTitleIcon()
        {
            var icon = cache.GetImage("Textures/UrhoIcon.png");
            Graphics.SetWindowIcon(icon);
            Graphics.WindowTitle = "UrhoSharp Sample";
        }

        void CreateConsoleAndDebugHud()
        {
            var xml = cache.GetXmlFile("UI/DefaultStyle.xml");
            console = Engine.CreateConsole();
            console.DefaultStyle = xml;
            console.Background.Opacity = 0.8f;

            debugHud = Engine.CreateDebugHud();
            debugHud.DefaultStyle = xml;
        }

        void HandleKeyDown(KeyDownEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Esc:
                    Exit();
                    return;
                case Key.F1:
                    console.Toggle();
                    return;
                case Key.F2:
                    debugHud.ToggleAll();
                    return;
            }

            var renderer = Renderer;
            switch (e.Key)
            {
                case Key.N1:
                    var quality = renderer.TextureQuality;
                    ++quality;
                    if (quality > 2)
                        quality = 0;
                    renderer.TextureQuality = quality;
                    break;

                case Key.N2:
                    var mquality = renderer.MaterialQuality;
                    ++mquality;
                    if (mquality > 2)
                        mquality = 0;
                    renderer.MaterialQuality = mquality;
                    break;

                case Key.N3:
                    renderer.SpecularLighting = !renderer.SpecularLighting;
                    break;

                case Key.N4:
                    renderer.DrawShadows = !renderer.DrawShadows;
                    break;

                case Key.N5:
                    var shadowMapSize = renderer.ShadowMapSize;
                    shadowMapSize *= 2;
                    if (shadowMapSize > 2048)
                        shadowMapSize = 512;
                    renderer.ShadowMapSize = shadowMapSize;
                    break;

                // shadow depth and filtering quality
                case Key.N6:
                    var q = (int)renderer.ShadowQuality++;
                    if (q > 3)
                        q = 0;
                    renderer.ShadowQuality = (ShadowQuality)q;
                    break;

                // occlusion culling
                case Key.N7:
                    var o = !(renderer.MaxOccluderTriangles > 0);
                    renderer.MaxOccluderTriangles = o ? 5000 : 0;
                    break;

                // instancing
                case Key.N8:
                    renderer.DynamicInstancing = !renderer.DynamicInstancing;
                    break;

                case Key.N9:
                    Image screenshot = new Image();
                    Graphics.TakeScreenShot(screenshot);
                    screenshot.SavePNG(FileSystem.ProgramDir + $"Data/Screenshot_{GetType().Name}_{DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture)}.png");
                    break;
            }
        }

        void InitTouchInput()
        {
            TouchEnabled = true;
            var layout = ResourceCache.GetXmlFile("UI/ScreenJoystick_Samples.xml");
            if (!string.IsNullOrEmpty(JoystickLayoutPatch))
            {
                XmlFile patchXmlFile = new XmlFile();
                patchXmlFile.FromString(JoystickLayoutPatch);
                layout.Patch(patchXmlFile);
            }
            var screenJoystickIndex = Input.AddScreenJoystick(layout, ResourceCache.GetXmlFile("UI/DefaultStyle.xml"));
            Input.SetScreenJoystickVisible(screenJoystickIndex, true);
        }
    }
}