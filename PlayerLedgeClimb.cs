using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerLedgeClimb : MonoBehaviour
{
    [Header("References")]
    public CharacterController controller;
    public MonoBehaviour normalMoveScript;
    public Animator animator;

    [Header("Climb Settings")]
    public float climbDuration = 0.6f;

    private Ledge currentLedge;
    private bool isHanging;
    private bool isClimbing;

    private void Start()
    {
        Debug.Log("PlayerLedgeClimb is running on: " + gameObject.name);
    }

    private void Update()
    {
        if (isClimbing)
            return;

        if (!isHanging)
        {
            if (currentLedge != null && GrabPressed())
            {
                EnterHang(currentLedge);
            }
        }
        else
        {
            if (ClimbPressed())
            {
                StartCoroutine(ClimbUp());
            }

            if (DropPressed())
            {
                DropFromLedge();
            }
        }
    }

    private bool GrabPressed()
    {
        return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
    }

    private bool ClimbPressed()
    {
        return Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
    }

    private bool DropPressed()
    {
        return Keyboard.current != null && Keyboard.current.sKey.wasPressedThisFrame;
    }

    private void EnterHang(Ledge ledge)
    {
        isHanging = true;

        if (normalMoveScript != null)
            normalMoveScript.enabled = false;

        if (controller != null)
            controller.enabled = false;

        transform.position = ledge.hangPoint.position;
        transform.rotation = ledge.hangPoint.rotation;

        if (animator != null)
            animator.SetBool("IsHanging", true);

        Debug.Log("Grabbed ledge: " + ledge.name);
    }

    private IEnumerator ClimbUp()
    {
        if (currentLedge == null)
            yield break;

        isClimbing = true;
        isHanging = false;

        if (animator != null)
        {
            animator.SetBool("IsHanging", false);
            animator.SetTrigger("ClimbUp");
        }

        Vector3 startPos = transform.position;
        Quaternion startRot = transform.rotation;

        Vector3 endPos = currentLedge.standPoint.position;
        Quaternion endRot = currentLedge.standPoint.rotation;

        float timer = 0f;

        while (timer < climbDuration)
        {
            timer += Time.deltaTime;
            float t = timer / climbDuration;
            t = Mathf.SmoothStep(0f, 1f, t);

            transform.position = Vector3.Lerp(startPos, endPos, t);
            transform.rotation = Quaternion.Slerp(startRot, endRot, t);

            yield return null;
        }

        transform.position = endPos;
        transform.rotation = endRot;

        if (controller != null)
            controller.enabled = true;

        if (normalMoveScript != null)
            normalMoveScript.enabled = true;

        isClimbing = false;

        Debug.Log("Climb finished");
    }

    private void DropFromLedge()
    {
        isHanging = false;

        if (animator != null)
            animator.SetBool("IsHanging", false);

        if (controller != null)
            controller.enabled = true;

        if (normalMoveScript != null)
            normalMoveScript.enabled = true;

        Debug.Log("Dropped from ledge");
    }

    private void OnTriggerEnter(Collider other)
    {
        Ledge ledge = other.GetComponentInParent<Ledge>();

        if (ledge != null)
        {
            currentLedge = ledge;
            Debug.Log("Entered ledge: " + ledge.name);
        }
    }

    private void OnTriggerStay(Collider other)
    {
        Ledge ledge = other.GetComponentInParent<Ledge>();

        if (ledge != null)
        {
            currentLedge = ledge;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        Ledge ledge = other.GetComponentInParent<Ledge>();

        if (ledge != null && ledge == currentLedge)
        {
            currentLedge = null;
            Debug.Log("Exited ledge: " + ledge.name);
        }
    }
}