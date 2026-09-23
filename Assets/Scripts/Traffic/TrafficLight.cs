using System.Collections.Generic;
using UnityEngine;

public class TrafficLight : MonoBehaviour
{
    public enum Signal { Red, Amber, Green }

    private readonly List<TrafficLightHead> heads = new List<TrafficLightHead>();

    private float greenSeconds = 10f;
    private float amberSeconds = 2.5f;
    private float allRedSeconds = 1.5f;
    private float clock;

    float HalfCycle => greenSeconds + amberSeconds + allRedSeconds;

    public void Configure(float green, float amber, float allRed)
    {
        greenSeconds = Mathf.Max(1f, green);
        amberSeconds = Mathf.Max(0f, amber);
        allRedSeconds = Mathf.Max(0f, allRed);

        clock = Random.Range(0f, HalfCycle * 2f);
    }

    public void AddHead(TrafficLightHead head, int side)
    {
        head.Bind(side);
        heads.Add(head);

        head.Show(SignalFor(side));
    }

    public Signal SignalFor(int side)
    {
        float t = Mathf.Repeat(clock, HalfCycle * 2f);

        bool northSouthTurn = t < HalfCycle;

        if (northSouthTurn != (side % 2 == 0)) return Signal.Red;

        float local = northSouthTurn ? t : t - HalfCycle;

        if (local < greenSeconds) return Signal.Green;
        if (local < greenSeconds + amberSeconds) return Signal.Amber;

        return Signal.Red;
    }

    public bool PedestrianMayCross(int side)
    {
        return SignalFor(side) == Signal.Red;
    }

    // Update is called once per frame
    void Update()
    {
        clock += Time.deltaTime;

        for (int i = 0; i < heads.Count; i++)
        {
            if (heads[i] != null)
                heads[i].Show(SignalFor(heads[i].Side));
        }
    }
}
