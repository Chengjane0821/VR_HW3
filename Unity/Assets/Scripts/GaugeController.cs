
using UnityEngine;

public class GaugeController : MonoBehaviour
{
    public ArduinoManager arduinoManager;
    public Transform needlePivot;

    public float minAngle = 135f;
    public float maxAngle = -135f;
    public float smoothSpeed = 10f;

    public int receivedPotValue;
    public float targetAngle;
    public float currentAngle = 135f;

    void Start()
    {
        if (arduinoManager == null) arduinoManager = FindFirstObjectByType<ArduinoManager>();

        if (needlePivot == null) needlePivot = transform.Find("NeedlePivot");

        if (arduinoManager == null)
            Debug.LogError("ArduinoManager not found!");

        if (needlePivot == null)
            Debug.LogError("NeedlePivot not found!");
    }

    void Update()
    {
        if (arduinoManager == null || needlePivot == null)
            return;

        receivedPotValue = arduinoManager.potValue;

        targetAngle = Mathf.Lerp(
            minAngle,
            maxAngle,
            receivedPotValue / 1023f
        );

        currentAngle = Mathf.Lerp(
            currentAngle,
            targetAngle,
            Time.deltaTime * smoothSpeed
        );

        needlePivot.localRotation =
            Quaternion.Euler(0, 0, currentAngle);
    }
}
