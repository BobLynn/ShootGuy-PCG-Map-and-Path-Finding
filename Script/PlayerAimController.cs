using UnityEngine;
using Unity.Cinemachine;
using UnityEngine.InputSystem;


public class PlayerAimController : MonoBehaviour
{
    [Header("Identity Reference")]
    private Player player;
    [Header("Cameras")]
    [SerializeField] private GameObject normalCamera;
    [SerializeField] private GameObject aimCamera;

    [Header("Cinemachine Target")]
    [Tooltip("Drag the Main Camera GameObject here to read its direction.")]
    [SerializeField] private Transform cameraTransform;

    [Header("Animation")]
    [Tooltip("Drag your player character model here (the one with the Animator component).")]
    [SerializeField] private Animator animator;


    public double cooldown = 1.0; // 冷卻時間，暫時共用
    [Header("Weapon Settings")]
    [Tooltip("Drag the Gun GameObject that is parented under the Right Hand bone here.")]
    [SerializeField] private GameObject weaponProp;
    public Transform shootPoint; // 子彈發射點，通常是槍口位置
    
    private double cooldownTimerBullet = 0; // 用來控制射擊頻率的計時器
    
    [Header("Coin Settings")]  
    public double tossForce = 10.0; // 硬幣丟出的初始力道
    private double cooldownTimerCoin = 0; // 用來控制丟硬幣頻率的計時器

    [Header("UI Crosshair Canvas Element")]
    [Tooltip("Drag your Canvas Crosshair Game Object here.")]
    [SerializeField] private GameObject uiCrosshair;

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = true;

        // Auto-fetch the animator on the same object if you forgot to assign it
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (weaponProp != null)
        {
            weaponProp.SetActive(false);
        }
        player = GetComponent<Player>();
    }

    private void Update()
    {
        // Safety check to make sure you dragged them into the inspector slots
        if (normalCamera == null || aimCamera == null) return;

        // Auto-fetch the main camera transform if you forgot to assign it
        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        // Grab the Cinemachine components off your GameObjects
        var normalCinemachine = normalCamera.GetComponent<CinemachineVirtualCamera>();
        var aimCinemachine = aimCamera.GetComponent<CinemachineVirtualCamera>();

        // Check if the mouse exists, then check the right button
        bool isRightClickPressed = Mouse.current != null && Mouse.current.rightButton.isPressed;

        if (isRightClickPressed)
        {
            aimCinemachine.Priority.Value = 15;
            // If we are aiming, force the character model to face the exact same direction the camera is looking!
            if (cameraTransform != null)
            {
                // Get the camera's current forward angle, but wipe out the up/down tilt (Y-axis only)
                Vector3 targetForward = cameraTransform.forward;
                targetForward.y = 0; // Stops the player from tilting face-first into the dirt when looking down

                if (targetForward != Vector3.zero)
                {
                    // Create the rotation rotation rotation mapping
                    Quaternion targetRotation = Quaternion.LookRotation(targetForward);

                    // Instantly snap (or smoothly slide) the player's body to match the camera
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 100f);
                }
            }
            if (player.currentEquipment == Player.EquipmentType.Gun)
            {
                if (animator != null)
                {
                    animator.SetBool("IsAiming", isRightClickPressed);
                }
                if (weaponProp != null)
                {
                    weaponProp.SetActive(isRightClickPressed);
                }
            }
        }
        else
        {
            aimCinemachine.Priority.Value = 5;
            if (animator != null)
            {
                animator.SetBool("IsAiming", false);
            }
            if (weaponProp != null)
            {
                weaponProp.SetActive(false);
            }
        }

        // Toggle the central UI crosshair to match your aim state perfectly!
        if (uiCrosshair != null)
        {
            uiCrosshair.SetActive(isRightClickPressed);
        }



        if (isRightClickPressed && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (player.currentEquipment == Player.EquipmentType.Gun && cooldownTimerBullet <= 0)
            {
                cooldownTimerBullet = cooldown; // 重置冷卻計時器
                
                //計算子彈髮色方向， 從攝影機的中心店發射ray， 如果有擊中物體就朝那個方向發射子彈， 沒有就朝攝影機的forward方向發射
                RaycastHit hit;
                if (Physics.Raycast(cameraTransform.position, cameraTransform.forward, out hit, 100f))
                {
                    Vector3 shootDirection = (hit.point - shootPoint.position).normalized;
                    ShootBullet(shootDirection);
                }
                else
                {
                    ShootBullet(cameraTransform.forward);
                }
            }
            else if (player.currentEquipment == Player.EquipmentType.Coin && cooldownTimerCoin <= 0)
            {
                cooldownTimerCoin = cooldown; // 重置冷卻計時器
                RaycastHit hit;
                if (Physics.Raycast(cameraTransform.position, cameraTransform.forward, out hit, 100f))
                {                     
                    Vector3 shootDirection = (hit.point - shootPoint.position).normalized;
                    TossCoin(shootDirection);
                }
                else
                {
                    TossCoin(cameraTransform.forward);
                }
            }
        }
        // 冷卻計時器遞減
        if (cooldownTimerBullet > 0)
        {
            cooldownTimerBullet -= Time.deltaTime;
            // Debug.Log($"{player.playerName} Cooldown Timer: {cooldownTimerBullet:F2} seconds remaining");
        }
        if (cooldownTimerCoin > 0)
        {
            cooldownTimerCoin -= Time.deltaTime;
            // Debug.Log($"{player.playerName} Cooldown Timer: {cooldownTimerCoin:F2} seconds remaining");
        }
        
    }
    private void ShootBullet(Vector3 direction)
    {
        if (animator != null){
            animator.SetTrigger("Shoot");
        }
        // 改成跟 Pool 借子彈：
        GameObject bullet = BulletPool.Instance.GetBullet(shootPoint.position, cameraTransform.rotation);
        
        if (bullet.TryGetComponent(out SimpleProjectile projectile))
        {
            projectile.Fire(direction, false);
            UnityEngine.Debug.Log($"{player.playerName} Bang! Fired pooled bullet at locked direction."); 
        }
        else{
            UnityEngine.Debug.Log($"{player.playerName} Failed to fire bullet: No SimpleProjectile component found on the pooled bullet.");   
        }
        // 減少Player bullet數量
        player.bulletCount--;
          
    }

    private void TossCoin(Vector3 direction)
    {
        // animator.SetTrigger("TossCoin");
        // 改成跟 Pool 借硬幣：
        GameObject coin = BulletPool.Instance.GetCoin(shootPoint.position, cameraTransform.rotation);
        double tossForce = this.tossForce; // 使用公共屬性設定的力道
        Vector3 tossDirection = cameraTransform.forward + cameraTransform.up * 0.5f; // 往前加上一點向上的力道，讓硬幣有個漂亮的拋物線
        
        if (coin.TryGetComponent(out Coin coinComponent))
        {
            coinComponent.Toss(tossForce, tossDirection);
            UnityEngine.Debug.Log($"{player.playerName} Tossed pooled coin at locked direction."); 
        }
        else{
            UnityEngine.Debug.Log($"{player.playerName} Failed to toss coin: No SimpleProjectile component found on the pooled coin.");   
        }
        // 減少Player coin數量
        player.coinCount--;
    }
}