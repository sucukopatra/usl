using UnityEngine;

public class FPController_CC : MonoBehaviour
{
    [Header("Movement Speeds")]
    [SerializeField] private float walkSpeed = 3.0f;
    [SerializeField] private float sprintMultiplier = 2.0f;

    [Header("Crouch")]
    [SerializeField] private float crouchHeight = 1.0f;
    [SerializeField] private float standingHeight = 1.8f;
    [SerializeField] private float crouchSpeedMultiplier = 0.8f;

    [Header("Jump Parameters")]
    [SerializeField] private float jumpHeight = 1.4f;
    [SerializeField] private float gravity = -24f;
    [SerializeField] private float jumpCutGravityMultiplier = 4f;

    [Header("Look Parameters")]
    [SerializeField] private float mouseSensitivity = 0.1f;
    [SerializeField] private float upDownLookRange = 80f;

    [Header ("References")]
    [SerializeField] private Camera mainCamera;

    private CharacterController characterController;
    private Vector3 currentMovement;
    private float currentSpeed;
    private float verticalRotation;

    void Start()
    {
        characterController = GetComponent<CharacterController>();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        HandleMovement();
        HandleRotation();
    }

    private void HandleJumping()
    {
        if (characterController.isGrounded)
        {
            if (currentMovement.y < 0)
               currentMovement.y = -2f;

            if (InputManager.Instance.JumpJustPressed)
            {
                currentMovement.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }
        }
        else
        {
            float currentGravity = gravity;

            if (!InputManager.Instance.JumpBeingHeld && currentMovement.y > 0)
            {
                currentGravity *= jumpCutGravityMultiplier;
            }

            currentMovement.y += currentGravity * Time.deltaTime;
        }
    }
    private void HandleCrouching()
    {
        if (InputManager.Instance.CrouchBeingHeld)
        {
            characterController.height = crouchHeight;
            currentSpeed *= crouchSpeedMultiplier;
        }
        else
        {
            characterController.height = standingHeight;
        }
    }

    private void HandleSprint()
    {
        if(InputManager.Instance.SprintBeingHeld) currentSpeed = walkSpeed * sprintMultiplier;
        else currentSpeed = walkSpeed;
    }

    private Vector3 CalculateWorldDirection()
    {
        Vector3 inputDirection = new Vector3(InputManager.Instance.MoveInput.x, 0f, InputManager.Instance.MoveInput.y);
        Vector3 worldDirection = transform.TransformDirection(inputDirection);
        return worldDirection.normalized;
    }

    private void HandleMovement()
    {
        HandleSprint();
        HandleCrouching();
        HandleJumping();

        Vector3 worldDirection = CalculateWorldDirection();
        currentMovement.x = worldDirection.x * currentSpeed;
        currentMovement.z = worldDirection.z * currentSpeed;

        characterController.Move(currentMovement * Time.deltaTime);
    }

    private void ApplyHorizontalRotation(float rotationAmount)
    {
        transform.Rotate(0, rotationAmount, 0);
    }

    private void ApplyVerticalRotation(float rotationAmount)
    {
        verticalRotation = Mathf.Clamp(verticalRotation - rotationAmount, -upDownLookRange, upDownLookRange);
        mainCamera.transform.localRotation = Quaternion.Euler(verticalRotation, 0, 0);
    }

    private void HandleRotation()
    {
        float mouseXRotation = InputManager.Instance.LookInput.x * mouseSensitivity;
        float mouseYRotation = InputManager.Instance.LookInput.y * mouseSensitivity;

        ApplyHorizontalRotation(mouseXRotation);
        ApplyVerticalRotation(mouseYRotation);
    }
}
