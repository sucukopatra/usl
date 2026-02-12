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

    [Header ("References")]

    private CharacterController characterController;
    private Vector3 currentMovement;
    private float currentSpeed;
    private float verticalRotation;
    private Transform _cameraTransform;

    void Start()
    {
        characterController = GetComponent<CharacterController>();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        _cameraTransform = Camera.main.transform;
    }

    void Update()
    {
        HandleMovement();
    }

    private void HandleJumping()
    {
        if (characterController.isGrounded)
        {
            if (currentMovement.y < 0)
               currentMovement.y = -2f;

            if (InputManager.Instance.JumpPressed)
            {
                currentMovement.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }
        }
        else
        {
            float currentGravity = gravity;

            if (!InputManager.Instance.JumpHeld && currentMovement.y > 0)
            {
                currentGravity *= jumpCutGravityMultiplier;
            }

            currentMovement.y += currentGravity * Time.deltaTime;
        }
    }
    private void HandleCrouching()
    {
        if (InputManager.Instance.CrouchHeld)
        {
            characterController.height = crouchHeight;
            currentSpeed = walkSpeed * crouchSpeedMultiplier;
        }
        else
        {
            characterController.height = standingHeight;
        }
    }

    private void HandleSprint()
    {
        if(InputManager.Instance.SprintHeld) currentSpeed = walkSpeed * sprintMultiplier;
        else currentSpeed = walkSpeed;
    }

    private Vector3 HandleCamera()
    {
        Vector3 moveDirection = new Vector3(InputManager.Instance.Move.x, 0f, InputManager.Instance.Move.y);
        moveDirection = _cameraTransform.forward * moveDirection.z + _cameraTransform.right * moveDirection.x;
        moveDirection.y = 0f;
        return moveDirection;
    }

    private void HandleMovement()
    {
        HandleSprint();
        HandleCrouching();
        HandleJumping();

        Vector3 moveDirection = HandleCamera();
        currentMovement.x = moveDirection.x * currentSpeed;
        currentMovement.z = moveDirection.z * currentSpeed;

        characterController.Move(currentMovement * Time.deltaTime);
    }
}
