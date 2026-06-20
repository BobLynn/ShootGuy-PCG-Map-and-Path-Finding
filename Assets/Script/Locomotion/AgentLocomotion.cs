using UnityEngine;
using KevinIglesias;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Agent))] // 確保它跟 Agent 掛在一起
public class AgentLocomotion : MonoBehaviour
{
    private Agent _agent;
    private CharacterController _controller;
    private Animator _animator;
    private bool _hasAnimator;


    [Header("Movement Status")]
    public Vector3 velocity; // 目前的實際速度 (開放給 Navigator 讀取)
    
    [Header("Rotation Settings")]
    [Tooltip("How fast the character turns to face movement direction")]
    [Range(0.0f, 0.3f)] public float RotationSmoothTime = 0.12f;
    private float _targetRotation = 0.0f;
    private float _rotationVelocity;

    [Header("Jump & Gravity")]
    public float JumpHeight = 0.5f;
    public float Gravity = -15.0f;
    public float JumpTimeout = 0.50f;
    public float FallTimeout = 0.15f;
    private float _verticalVelocity;
    private float _terminalVelocity = 53.0f;
    private float _jumpTimeoutDelta;
    private float _fallTimeoutDelta;

    [Header("Grounded Check")]
    public bool Grounded = true;
    public float GroundedOffset = -0.14f;
    public float GroundedRadius = 0.28f;
    public LayerMask GroundLayers;

    [Header("Animation Settings")]
    public float speedChangeRate = 10f;
    private float _animationBlend;
    private int _animIDSpeed, _animIDGrounded, _animIDJump, _animIDFreeFall, _animIDMotionSpeed;

    void Awake()
    {
        _agent = GetComponent<Agent>();
        _controller = GetComponent<CharacterController>();
        _hasAnimator = TryGetComponent(out _animator);

        // wanderAngle = Random.Range(0f, 360f);

        AssignAnimationIDs();
        _jumpTimeoutDelta = JumpTimeout;
        _fallTimeoutDelta = FallTimeout;
    }

    void Update()
    {
        JumpAndGravity();
        GroundedCheck();
        ApplyMovementAndRotation(Time.deltaTime);
    }

    /// <summary>
    /// 核心移動執行：接收 Agent 算好的 steeringForce 並移動
    /// </summary>
    private void ApplyMovementAndRotation(float dt)
    {
        // ===============================================
        // 1. 物理推力整合 (Steering Force to Velocity)
        // ===============================================

        if (_agent.health <= 0)
        {
            // 如果死亡了，強制煞車，不接受任何導航或側步力
            velocity = Vector3.Lerp(velocity, Vector3.zero, dt * 5f); // 快速減速到停止
            _agent.steeringForce = Vector3.zero; // 不再接受新的推力
        }

        // ✨ [核心修復]: 將「旋轉鎖定」與「位移鎖定」徹底解耦
        if (_agent.brain.currentDecision == AgentDecision.ATTACK && !_agent.isStrafing && _agent.isTurningInPlace)
        {
            // 如果是在攻擊階段的瞄準，且沒有在側步 -> 強制煞車站穩開槍
            velocity = Vector3.zero; 
        }
        else if (_agent.isTurningInPlace && !_agent.isStrafing)
        {
            // 一般的原地轉向 (例如尋找聲音來源) -> 給予摩擦力平滑煞車
            velocity = Vector3.Lerp(velocity, Vector3.zero, dt * 10f); 
        }
        else
        {
            // 只要不是上述被強制卡死的狀況，就接受 Navigator 算出來的 Steering Force！
            // 這包含了普通的 Seek, PathFollowing，以及我們的 Tactical Strafing 側步力！
            velocity += _agent.steeringForce * dt; 
        }

        // 2. 限制水平最大速度 (由 Agent 的狀態決定 maxSpeed)
        Vector3 horizontalVelocity = new Vector3(velocity.x, 0.0f, velocity.z);
        if (horizontalVelocity.sqrMagnitude > _agent.maxSpeed * _agent.maxSpeed)
        {
            horizontalVelocity = horizontalVelocity.normalized * _agent.maxSpeed;
            velocity = new Vector3(horizontalVelocity.x, velocity.y, horizontalVelocity.z);
        }

        // 3. 執行 CharacterController 移動
        Vector3 finalMovement = (horizontalVelocity * dt) + (new Vector3(0.0f, _verticalVelocity, 0.0f) * dt);
        _controller.Move(finalMovement);

        // 4. 更新朝向 (平滑旋轉)
        if (_agent.isTurningInPlace)
        {
            Vector3 dir = _agent.turnTargetPos - transform.position;
            dir.y = 0;
            if (dir.sqrMagnitude > 0.01f)
            {
                _targetRotation = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                float rotation = Mathf.SmoothDampAngle(transform.eulerAngles.y, _targetRotation, ref _rotationVelocity, RotationSmoothTime);
                transform.rotation = Quaternion.Euler(0.0f, rotation, 0.0f);
            }
        }
        else if (horizontalVelocity.sqrMagnitude > 0.1f)
        {
            _targetRotation = Mathf.Atan2(horizontalVelocity.x, horizontalVelocity.z) * Mathf.Rad2Deg;
            float rotation = Mathf.SmoothDampAngle(transform.eulerAngles.y, _targetRotation, ref _rotationVelocity, RotationSmoothTime);
            transform.rotation = Quaternion.Euler(0.0f, rotation, 0.0f);
        }

        if (_hasAnimator)
        {
            float speed = horizontalVelocity.magnitude;

            // 保留原本的 BlendTree 更新 (如果你有使用混合樹的話)
            _animationBlend = Mathf.Lerp(_animationBlend, speed, dt * speedChangeRate);
            if (_animationBlend < 0.01f) _animationBlend = 0f;
            _animator.SetFloat(_animIDSpeed, _animationBlend);
            _animator.SetFloat(_animIDMotionSpeed, 1f);
        }
    }

    private void GroundedCheck()
    {
        Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z);
        Grounded = Physics.CheckSphere(spherePosition, GroundedRadius, GroundLayers, QueryTriggerInteraction.Ignore);

        if (_hasAnimator) _animator.SetBool(_animIDGrounded, Grounded);
    }

    private void JumpAndGravity()
    {
        if (Grounded)
        {
            _fallTimeoutDelta = FallTimeout;
            if (_hasAnimator)
            {
                _animator.SetBool(_animIDJump, false);
                _animator.SetBool(_animIDFreeFall, false);
            }
            if (_verticalVelocity < 0.0f) _verticalVelocity = -2f;

            // 讀取大腦/導航下達的跳躍指令
            if (_agent.wantsToJump && _jumpTimeoutDelta <= 0.0f)
            {
                _verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);
                if (_hasAnimator) _animator.SetBool(_animIDJump, true);
                _agent.wantsToJump = false; // 執行跳躍後重置指令
            }

            if (_jumpTimeoutDelta >= 0.0f) _jumpTimeoutDelta -= Time.deltaTime;
        }
        else
        {
            _jumpTimeoutDelta = JumpTimeout;
            if (_fallTimeoutDelta >= 0.0f) _fallTimeoutDelta -= Time.deltaTime;
            else if (_hasAnimator) _animator.SetBool(_animIDFreeFall, true);
            
            _agent.wantsToJump = false;
        }

        if (_verticalVelocity < _terminalVelocity)
            _verticalVelocity += Gravity * Time.deltaTime;
    }

    private void AssignAnimationIDs()
    {
        _animIDSpeed = Animator.StringToHash("Speed");
        _animIDGrounded = Animator.StringToHash("Grounded");
        _animIDJump = Animator.StringToHash("Jump");
        _animIDFreeFall = Animator.StringToHash("FreeFall");
        _animIDMotionSpeed = Animator.StringToHash("MotionSpeed");
    }

    /// <summary>
    /// 【新增】供外部腳本 (如 AgentBrain) 呼叫單次動作動畫
    /// </summary>
    public void PlayAction(SoldierAction action)
    {
        if (_hasAnimator) _animator.SetTrigger(action.ToString());
    }

    // 動畫事件接收
    private void OnFootstep(AnimationEvent animationEvent) { }
    private void OnLand(AnimationEvent animationEvent) { }
}