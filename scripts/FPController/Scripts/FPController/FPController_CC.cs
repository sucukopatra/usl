using System;
using UnityEngine;
using Unity.Cinemachine;

/// <summary>
/// FPController_CC
/// ----------------
/// A first-person CharacterController locomotion script that aims to be:
/// - Production-tight (stable, deterministic update order)
/// - Still readable (clear structure, comments explain "why" not "what is a float")
///
/// Core ideas:
/// - Input is read once per frame (cached).
/// - Grounding is checked via physics (more reliable than CharacterController.isGrounded).
/// - Crouch smoothly changes capsule height/center, blocked by overhead geometry.
/// - Jump feels responsive via:
///     * coyote time   (late jumps after leaving ground)
///     * jump buffer   (early jumps just before landing)
///     * jump cut      (short hops when releasing jump while rising)
/// - Camera/head target is driven by capsule height + optional head bob.
/// - Events: OnFootstep, OnLanded, OnJumped
///
/// Update flow (per frame):
/// ReadInput → CheckGrounded → ResolveState → UpdateCrouch →
/// [CheckGrounded → ResolveState if capsule changed] →
/// UpdateHeadTarget → UpdateMove → UpdateHeadBob →
/// UpdateJumpGravity → UpdateFootstepEvent → ApplyMotion
///
/// IMPORTANT: UpdateHeadTarget must run before UpdateHeadBob.
/// UpdateHeadTarget computes _headBaseLocal; UpdateHeadBob applies the bob
/// offset and performs the only write to headTarget.localPosition.
///
/// NOTE: Ground transition detection (_wasGroundedLastFrame) lives inside
/// UpdateGroundTransitionsAndJumpTimers rather than at the top of Update.
/// If a future system also needs to react to landing/leaving-ground events,
/// consider centralizing it or using the existing OnLanded/OnJumped events.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FPController_CC : MonoBehaviour
{
    // ──────────────────────────────────────────────────────────────
    // Serialized Fields
    // ──────────────────────────────────────────────────────────────

    [Header("References")]
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private Transform headTarget;

    [Header("Camera Sensitivity")]
    [SerializeField] private float sensitivity = 25f;

    [Header("Head Bob")]
    [SerializeField] private bool  headBobEnabled    = true;
    [SerializeField] private float headBobFrequency  = 7f;
    [SerializeField] private float headBobAmplitude  = 0.003f;
    [SerializeField] private float headBobFadeSpeed  = 8f;

    [Header("Footsteps")]
    [Tooltip("Distance traveled per footstep when head bob is disabled.")]
    [SerializeField] private float footstepDistance = 2.0f;

    [Header("Movement Speeds")]
    [SerializeField] private float walkSpeed   = 3.0f;
    [SerializeField] private float sprintSpeed = 6.0f;
    [SerializeField] private float crouchSpeed = 2.4f;

    [Header("Movement Smoothing")]
    [SerializeField] private float speedChangeRate    = 10.0f;
    [SerializeField] private float speedSnapThreshold = 0.1f;

    [Header("Analog Movement")]
    [Tooltip("Enable for gamepad sticks. Keyboard always uses full deflection.")]
    [SerializeField] private bool useAnalogMovement = false;

    [Header("Jump & Gravity")]
    [SerializeField] private float jumpHeight               = 1.4f;
    [SerializeField] private float gravity                  = -24f;
    [SerializeField] private float terminalVelocity         = -53f;  // signed floor, not a magnitude
    [SerializeField] private float coyoteTime               = 0.15f;
    [SerializeField] private float jumpBufferTime           = 0.15f;
    [SerializeField] private float jumpCutGravityMultiplier = 4f;    // applied when jump is released while rising

    [Header("Capsule Geometry")]
    [SerializeField] private float   standingHeight      = 1.8f;
    [SerializeField] private float   crouchHeight        = 1.0f;
    [SerializeField] private Vector3 standingCenterLocal = new Vector3(0f, 0.9f, 0f);

    [Header("Ground Check")]
    [SerializeField] private float     groundCheckDownOffset = 0.14f;
    [SerializeField] private float     groundCheckRadius     = 0.50f;
    [SerializeField] private LayerMask groundLayers          = 1;

    [Header("Crouch")]
    [SerializeField] private float     crouchLerpSpeed   = 12f;
    [SerializeField] private float     snapEpsilon       = 0.01f;
    // NOTE: groundLayers and obstructionLayers are intentionally separate.
    // Make sure they are configured correctly; mismatches can cause crouch-stuck bugs.
    [SerializeField] private LayerMask obstructionLayers = 1;

    [Header("Camera Height Follow")]
    [SerializeField] private float headHeightOffset = -0.1f;
    [SerializeField] private float headFollowSpeed  = 20f;

    // ──────────────────────────────────────────────────────────────
    // Events
    // ──────────────────────────────────────────────────────────────

    /// <summary>Fired once per footfall. Bob-synced when bob is enabled, distance-based otherwise.</summary>
    public event Action OnFootstep;

    /// <summary>Fired on the frame the player lands.</summary>
    public event Action OnLanded;

    /// <summary>Fired on the frame a jump executes.</summary>
    public event Action OnJumped;

    // ──────────────────────────────────────────────────────────────
    // Public State (useful for UI/debug/other systems)
    // ──────────────────────────────────────────────────────────────

    public enum MovementState { Normal, Crouching, Airborne }

    public MovementState State    { get; private set; }
    public bool          Grounded { get; private set; }
    public float         Speed    { get; private set; }
    public Vector3       MoveDir  { get; private set; }

    // ──────────────────────────────────────────────────────────────
    // Private State
    // ──────────────────────────────────────────────────────────────

    private CharacterController            _controller;
    private CinemachineInputAxisController _axisController;

    // Vertical motion is tracked manually; CharacterController does not handle gravity for you.
    private float _verticalVelocity;

    // Jump feel helpers
    private float _coyoteTimer;
    private float _jumpBufferTimer;
    private bool  _hasJumped;
    private bool  _wasGroundedLastFrame;

    // Head bob & footstep state
    private float   _bobTimer;
    private float   _bobAmplitudeScale; // fades bob in/out smoothly
    private float   _distanceTraveled;

    // Cached centers for standing vs crouching capsule
    private Vector3 _standingCenter;
    private Vector3 _crouchingCenter;

    // UpdateHeadTarget() produces _headBaseLocal (capsule-driven height).
    // UpdateHeadBob() consumes it and performs the only write to headTarget.localPosition.
    // IMPORTANT: UpdateHeadTarget must always run before UpdateHeadBob. (See Update flow above.)
    private Vector3 _headBaseLocal;

    // ──────────────────────────────────────────────────────────────
    // Input Cache (read once, use everywhere)
    // ──────────────────────────────────────────────────────────────

    private struct FrameInput
    {
        public Vector2 move;
        public bool    sprintHeld;
        public bool    crouchHeld;
        public bool    jumpPressed; // true only on the frame pressed
        public bool    jumpHeld;    // true while held
    }

    private FrameInput _input;

    // ──────────────────────────────────────────────────────────────
    // Constants
    // ──────────────────────────────────────────────────────────────

    private const string SensitivityPrefKey = "CameraSensitivity";

    // ──────────────────────────────────────────────────────────────
    // Unity Lifecycle
    // ──────────────────────────────────────────────────────────────

    private void Awake()
    {
        // CharacterController + non-unit scale is a Unity footgun.
        // We don't attempt to "fix" it with homemade scaling math, because that's worse.
        const float scaleEps = 0.0005f;
        Vector3 s = transform.lossyScale;
        bool nonUnit =
            Mathf.Abs(s.x - 1f) > scaleEps ||
            Mathf.Abs(s.y - 1f) > scaleEps ||
            Mathf.Abs(s.z - 1f) > scaleEps;

        if (nonUnit)
        {
            Debug.LogWarning(
                $"FPController_CC: Non-unit scale detected on '{name}' (lossyScale={s}). " +
                "CharacterController expects scale (1,1,1). Fix the hierarchy scale to avoid grounding/capsule issues.",
                this
            );
        }

        _controller = GetComponent<CharacterController>();

        if (!cameraTransform && Camera.main)
            cameraTransform = Camera.main.transform;

        // Initialize capsule to standing dimensions.
        _controller.height = standingHeight;
        _controller.center = standingCenterLocal;

        _standingCenter = standingCenterLocal;

        // When crouching, shrinking height would normally lift the feet off the ground.
        // Shifting center downward by half the height delta keeps feet planted.
        float halfDelta  = (standingHeight - crouchHeight) * 0.5f;
        _crouchingCenter = _standingCenter - new Vector3(0f, halfDelta, 0f);

        _axisController = cameraTransform
            ? cameraTransform.GetComponentInParent<CinemachineInputAxisController>()
            : null;

        LoadSensitivity();
    }

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        _coyoteTimer          = coyoteTime;
        _wasGroundedLastFrame = Grounded;

        if (headTarget) _headBaseLocal = headTarget.localPosition;

        CheckGrounded();
        UpdateHeadTarget();
    }

    private void Update()
    {
        ReadInput();
        CheckGrounded();
        ResolveState();

        // Crouch can change the capsule, so we optionally re-evaluate grounding/state after it moves.
        bool capsuleChanged = UpdateCrouch();
        if (capsuleChanged)
        {
            CheckGrounded();
            ResolveState();
        }

        // IMPORTANT: UpdateHeadTarget must run before UpdateHeadBob (see class header).
        UpdateHeadTarget();
        UpdateMove();
        UpdateHeadBob();
        UpdateJumpGravity();
        UpdateFootstepEvent();
        ApplyMotion();
    }

    private void LateUpdate()
    {
        // Body yaw follows the camera yaw (typical FPS setup).
        if (cameraTransform)
            transform.rotation = Quaternion.Euler(0f, cameraTransform.eulerAngles.y, 0f);
    }

    // ──────────────────────────────────────────────────────────────
    // Input
    // ──────────────────────────────────────────────────────────────

    private void ReadInput()
    {
        // InputManager is user-defined; if it isn't present, input becomes default (zero).
        if (InputManager.Instance == null) { _input = default; return; }

        _input = new FrameInput
        {
            move        = InputManager.Instance.Move,
            sprintHeld  = InputManager.Instance.SprintHeld,
            crouchHeld  = InputManager.Instance.CrouchHeld,
            jumpPressed = InputManager.Instance.JumpPressed,
            jumpHeld    = InputManager.Instance.JumpHeld,
        };
    }

    // ──────────────────────────────────────────────────────────────
    // Grounded / State
    // ──────────────────────────────────────────────────────────────

    private void CheckGrounded()
    {
        // We use a physics check instead of CharacterController.isGrounded because isGrounded
        // can flicker on stairs, slopes, and edges.
        Vector3 bottom    = CapsuleBottom(CurrentCapsuleWorldCenter, _controller.height * 0.5f, _controller.radius);
        Vector3 spherePos = bottom + Vector3.down * groundCheckDownOffset;

        Grounded = Physics.CheckSphere(
            spherePos,
            groundCheckRadius,
            groundLayers,
            QueryTriggerInteraction.Ignore
        );
    }

    private void ResolveState()
    {
        if (!Grounded)
        {
            State = MovementState.Airborne;
            return;
        }

        State = _input.crouchHeld ? MovementState.Crouching : MovementState.Normal;
    }

    // ──────────────────────────────────────────────────────────────
    // Crouch
    // ──────────────────────────────────────────────────────────────

    private bool UpdateCrouch()
    {
        // Typical FPS behavior: crouch only while grounded (optional design choice).
        bool  wantCrouch   = Grounded && _input.crouchHeld;
        float targetHeight = wantCrouch ? crouchHeight     : standingHeight;
        Vector3 targetCenter = wantCrouch ? _crouchingCenter : _standingCenter;

        // If we're trying to stand up but something is above us, stay crouched.
        if (!wantCrouch && !CanStandUp())
            return false;

        float prevHeight = _controller.height;
        Vector3 prevCenter = _controller.center;

        // SnapLerp gives smooth movement but stops jitter near the end by snapping when close enough.
        float t = Time.deltaTime * crouchLerpSpeed;
        _controller.height = SnapLerp(_controller.height, targetHeight, t, snapEpsilon);
        _controller.center = SnapLerp(_controller.center, targetCenter, t, snapEpsilon);

        return _controller.height != prevHeight || _controller.center != prevCenter;
    }

    private bool CanStandUp()
    {
        // Already standing (or close enough): nothing to check.
        if (Mathf.Abs(_controller.height - standingHeight) < snapEpsilon)
            return true;

        // Cast a standing-sized capsule where we'd be if we stood up.
        float radius = _controller.radius;
        float half   = standingHeight * 0.5f;

        Vector3 wc     = StandingCapsuleWorldCenter;
        Vector3 capBot = CapsuleBottom(wc, half, radius);
        Vector3 capTop = wc + Vector3.up * (half - radius);

        return !Physics.CheckCapsule(
            capBot,
            capTop,
            radius,
            obstructionLayers,
            QueryTriggerInteraction.Ignore
        );
    }

    // ──────────────────────────────────────────────────────────────
    // Head Target (capsule-driven height)
    // ──────────────────────────────────────────────────────────────

    // Produces _headBaseLocal (consumed by UpdateHeadBob).
    private void UpdateHeadTarget()
    {
        if (!headTarget) return;

        // Base head height follows the capsule: center + half height + offset.
        float targetLocalY = _controller.center.y + _controller.height * 0.5f + headHeightOffset;

        // Exponential smoothing: frame-rate independent, snappy but stable.
        _headBaseLocal.y = ExpSmooth(_headBaseLocal.y, targetLocalY, headFollowSpeed);
    }

    // ──────────────────────────────────────────────────────────────
    // Head Bob (visual motion + footstep sync)
    // ──────────────────────────────────────────────────────────────

    // Consumes _headBaseLocal and performs the only write to headTarget.localPosition.
    private void UpdateHeadBob()
    {
        if (!headTarget) return;

        float speedRatio = walkSpeed > 0.0001f ? Speed / walkSpeed : 0f;

        // We only bob when grounded and moving meaningfully.
        bool shouldBob = headBobEnabled && Grounded && speedRatio >= 0.1f;

        // Fade bob in/out so enabling/disabling doesn't snap the camera.
        _bobAmplitudeScale = ExpSmooth(_bobAmplitudeScale, shouldBob ? 1f : 0f, headBobFadeSpeed);

        if (_bobAmplitudeScale >= 0.001f)
        {
            // We use a sine wave to simulate footfall rhythm.
            // Each upward zero-crossing corresponds to a "step" event.
            float prevSin = Mathf.Sin(_bobTimer * 2f);

            _bobTimer += Time.deltaTime * headBobFrequency * Mathf.Clamp(speedRatio, 0.8f, 1.3f);

            float curSin = Mathf.Sin(_bobTimer * 2f);

            if (prevSin < 0f && curSin >= 0f)
                OnFootstep?.Invoke();

            float scale      = _bobAmplitudeScale * Mathf.Clamp(speedRatio, 0f, 1.5f);
            float moveAmount = Mathf.Clamp01(_input.move.magnitude);

            float bobX = Mathf.Cos(_bobTimer) * (headBobAmplitude * 0.5f) * scale * moveAmount;
            float bobY = curSin               *  headBobAmplitude          * scale;

            headTarget.localPosition = _headBaseLocal + new Vector3(bobX, bobY, 0f);
        }
        else
        {
            headTarget.localPosition = _headBaseLocal;
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Footstep Event (distance fallback when bob is disabled)
    // ──────────────────────────────────────────────────────────────

    private void UpdateFootstepEvent()
    {
        // When head bob is enabled, footsteps are driven by the bob zero-crossing.
        if (headBobEnabled || !Grounded || Speed < 0.1f)
        {
            _distanceTraveled = 0f;
            return;
        }

        // Distance-based footsteps are simple and robust, but not rhythm-accurate.
        _distanceTraveled += Speed * Time.deltaTime;

        if (_distanceTraveled >= footstepDistance)
        {
            OnFootstep?.Invoke();
            _distanceTraveled = 0f;
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Movement (horizontal)
    // ──────────────────────────────────────────────────────────────

    private void UpdateMove()
    {
        float inputScale = useAnalogMovement ? Mathf.Clamp01(_input.move.magnitude) : 1f;

        float desiredSpeed =
            _input.move == Vector2.zero
                ? 0f
                : GetTargetSpeed() * inputScale;

        // Frame-rate independent smoothing:
        // Instead of Lerp(current, target, dt * rate), we use exp smoothing like other parts of this script.
        if (Mathf.Abs(Speed - desiredSpeed) > speedSnapThreshold)
            Speed = ExpSmooth(Speed, desiredSpeed, speedChangeRate);
        else
            Speed = desiredSpeed;

        MoveDir = _input.move == Vector2.zero
            ? Vector3.zero
            : ComputeMoveDirection(_input.move);
    }

    private float GetTargetSpeed()
    {
        if (Grounded && _input.crouchHeld) return crouchSpeed;
        return _input.sprintHeld ? sprintSpeed : walkSpeed;
    }

    private Vector3 ComputeMoveDirection(Vector2 input)
    {
        if (!cameraTransform) return Vector3.zero;

        // Camera-relative movement: forward/right projected onto XZ plane.
        Vector3 forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
        Vector3 right   = Vector3.ProjectOnPlane(cameraTransform.right,   Vector3.up).normalized;

        return Vector3.ClampMagnitude(forward * input.y + right * input.x, 1f);
    }

    // ──────────────────────────────────────────────────────────────
    // Jump & Gravity (vertical)
    // ──────────────────────────────────────────────────────────────

    private void UpdateJumpGravity()
    {
        UpdateGroundTransitionsAndJumpTimers();
        TryConsumeBufferedOrCoyoteJump();
        ApplyGravityWithJumpCut();
    }

    /// <summary>
    /// Detects landing/leaving-ground transitions, fires related events,
    /// and ticks both the coyote and jump-buffer timers.
    ///
    /// NOTE: _wasGroundedLastFrame lives here rather than at the top of Update.
    /// If a future system needs to react to these transitions independently,
    /// consider centralizing grounding events or subscribing to OnLanded/OnJumped.
    /// </summary>
    private void UpdateGroundTransitionsAndJumpTimers()
    {
        bool landedThisFrame     = Grounded  && !_wasGroundedLastFrame;
        bool leftGroundThisFrame = !Grounded &&  _wasGroundedLastFrame;
        _wasGroundedLastFrame    = Grounded;

        if (landedThisFrame)
        {
            _hasJumped = false;
            OnLanded?.Invoke();
        }

        if (leftGroundThisFrame)
            _coyoteTimer = coyoteTime;

        // Jump buffering: if you press jump slightly before landing,
        // we remember it for a short window so it still registers.
        _jumpBufferTimer = _input.jumpPressed
            ? jumpBufferTime
            : Mathf.Max(0f, _jumpBufferTimer - Time.deltaTime);

        if (Grounded)
        {
            // When grounded we don't want to accumulate downward velocity forever.
            // A small negative keeps the controller "stuck" to slopes instead of hovering.
            _coyoteTimer = 0f;
            if (_verticalVelocity < 0f) _verticalVelocity = -2f;
        }
        else if (_coyoteTimer > 0f)
        {
            _coyoteTimer = Mathf.Max(0f, _coyoteTimer - Time.deltaTime);
        }
    }

    /// <summary>
    /// Executes a jump if there is a buffered press and the player is either
    /// grounded or still within the coyote time window.
    /// </summary>
    private void TryConsumeBufferedOrCoyoteJump()
    {
        if (_hasJumped)           return;
        if (_jumpBufferTimer <= 0f) return;

        // Either grounded, or still within the coyote grace window.
        bool canJumpNow = Grounded || _coyoteTimer > 0f;
        if (!canJumpNow) return;

        ExecuteJump();
        _coyoteTimer = 0f; // consume the coyote window when used mid-air
    }

    /// <summary>
    /// Applies gravity each frame, scaling it up when the player releases
    /// jump while still rising (jump-cut) for snappier short hops.
    /// </summary>
    private void ApplyGravityWithJumpCut()
    {
        // Jump-cut: releasing jump while rising increases gravity so you get short hops.
        float gravityScale =
            !Grounded && !_input.jumpHeld && _verticalVelocity > 0f
                ? jumpCutGravityMultiplier
                : 1f;

        // Apply gravity until we reach terminal velocity.
        if (_verticalVelocity > terminalVelocity)
            _verticalVelocity += gravity * gravityScale * Time.deltaTime;
    }

    private void ExecuteJump()
    {
        // v = sqrt(h * -2 * g)
        // Choose the exact upward velocity needed to reach the desired apex height.
        _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);

        _jumpBufferTimer = 0f;
        _hasJumped       = true;

        OnJumped?.Invoke();
    }

    private void ApplyMotion()
    {
        // CharacterController.Move expects a displacement, not a velocity.
        Vector3 velocity = MoveDir * Speed + Vector3.up * _verticalVelocity;
        _controller.Move(velocity * Time.deltaTime);
    }

    // ──────────────────────────────────────────────────────────────
    // Sensitivity (Cinemachine axis gain)
    // ──────────────────────────────────────────────────────────────

    public void SetSensitivity(float value)
    {
        sensitivity = value;
        PlayerPrefs.SetFloat(SensitivityPrefKey, value);
        PlayerPrefs.Save();
        ApplySensitivity();
    }

    private void LoadSensitivity()
    {
        sensitivity = PlayerPrefs.GetFloat(SensitivityPrefKey, sensitivity);
        ApplySensitivity();
    }

    private void ApplySensitivity()
    {
        if (_axisController == null) return;

        foreach (var controller in _axisController.Controllers)
        {
            // Preserve axis inversion; if gain is negative, keep it negative.
            float sign = controller.Input.Gain < 0f ? -1f : 1f;
            controller.Input.Gain = sign * sensitivity;
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────

    private Vector3 CurrentCapsuleWorldCenter  => transform.TransformPoint(_controller.center);
    private Vector3 StandingCapsuleWorldCenter => transform.TransformPoint(_standingCenter);

    private static Vector3 CapsuleBottom(Vector3 worldCenter, float halfHeight, float radius)
        => worldCenter + Vector3.down * (halfHeight - radius);

    /// <summary>
    /// Exponential smoothing (frame-rate independent).
    /// Higher sharpness = snappier response.
    ///
    /// Why this exists:
    /// - Mathf.Lerp(current, target, dt * rate) changes "feel" depending on framerate.
    /// - This form behaves consistently at 30fps and 144fps.
    /// </summary>
    private static float ExpSmooth(float current, float target, float sharpness)
    {
        float a = 1f - Mathf.Exp(-sharpness * Time.deltaTime);
        return Mathf.Lerp(current, target, a);
    }

    private static float SnapLerp(float current, float target, float t, float eps)
    {
        float v = Mathf.Lerp(current, target, t);
        return Mathf.Abs(v - target) < eps ? target : v;
    }

    private static Vector3 SnapLerp(Vector3 current, Vector3 target, float t, float eps)
    {
        Vector3 v = Vector3.Lerp(current, target, t);
        return (v - target).sqrMagnitude < eps * eps ? target : v;
    }

    // ──────────────────────────────────────────────────────────────
    // Gizmos (debug visuals)
    // ──────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        // Resolve defensively so the gizmo is accurate in both edit mode and play mode.
        var     cc     = _controller != null ? _controller : GetComponent<CharacterController>();
        float   height = cc != null ? cc.height : standingHeight;
        float   radius = cc != null ? cc.radius : 0.5f;
        Vector3 center = cc != null ? transform.TransformPoint(cc.center) : transform.TransformPoint(standingCenterLocal);

        Gizmos.color = Application.isPlaying
            ? (Grounded ? new Color(0f, 1f, 0f, 0.35f) : new Color(1f, 0f, 0f, 0.35f))
            : new Color(0.5f, 0.5f, 0.5f, 0.35f);

        Vector3 bottom = CapsuleBottom(center, height * 0.5f, radius);
        Gizmos.DrawSphere(bottom + Vector3.down * groundCheckDownOffset, groundCheckRadius);

        if (!Application.isPlaying || State != MovementState.Crouching || cc == null) return;

        // Yellow ghost capsule shows where the standing obstruction check is cast.
        float   half   = standingHeight * 0.5f;
        Vector3 wc     = StandingCapsuleWorldCenter;
        Vector3 capBot = CapsuleBottom(wc, half, radius);
        Vector3 capTop = wc + Vector3.up * (half - radius);

        Gizmos.color = new Color(1f, 1f, 0f, 0.25f);
        Gizmos.DrawSphere(capBot, radius);
        Gizmos.DrawSphere(capTop, radius);
        Gizmos.DrawLine(capBot, capTop);
    }

    // ──────────────────────────────────────────────────────────────
    // Editor validation (keeps values sane)
    // ──────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnValidate()
    {
        var   cc   = GetComponent<CharacterController>();
        float minH = cc != null ? 2f * cc.radius + 0.01f : 0.2f;

        standingHeight = Mathf.Max(standingHeight, minH);
        crouchHeight   = Mathf.Clamp(crouchHeight, cc != null ? minH : 0.1f, standingHeight - 0.01f);

        groundCheckRadius = Mathf.Max(groundCheckRadius, 0.01f);
        snapEpsilon       = Mathf.Max(snapEpsilon, 0.0001f);
        footstepDistance  = Mathf.Max(footstepDistance, 0.1f);

        // If standingCenterLocal looks like the default convention (centered on X/Z, Y = half height),
        // keep it in sync when standingHeight changes. Intentional offsets are left alone.
        float expectedY = standingHeight * 0.5f;
        bool  isDefault =
            Mathf.Abs(standingCenterLocal.x) < 0.001f &&
            Mathf.Abs(standingCenterLocal.z) < 0.001f &&
            Mathf.Abs(standingCenterLocal.y - expectedY) < 0.005f;

        if (isDefault)
            standingCenterLocal = new Vector3(0f, expectedY, 0f);
    }
#endif
}
