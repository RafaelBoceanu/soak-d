using UnityEngine;

[DisallowMultipleComponent]
public class TrafficLightHead : MonoBehaviour
{
    [Tooltip("Lit lamp objects. Only the one for the current signal is on")]
    [SerializeField] private GameObject redLamp;
    [SerializeField] private GameObject amberLamp;
    [SerializeField] private GameObject greenLamp;

    private bool hasShown;
    private TrafficLight.Signal shown;

    public int Side { get; private set; }

    public void Bind(int side)
    {
        Side = side;
        hasShown = false;
    }

    public void Show(TrafficLight.Signal signal)
    {
        if (hasShown && shown == signal) return;

        hasShown = true;
        shown = signal;

        if (redLamp != null) redLamp.SetActive(signal == TrafficLight.Signal.Red);
        if (amberLamp != null) amberLamp.SetActive(signal == TrafficLight.Signal.Amber);
        if (greenLamp != null) greenLamp.SetActive(signal == TrafficLight.Signal.Green);
    }
}
