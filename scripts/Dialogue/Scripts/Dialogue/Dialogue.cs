using System;
using UnityEngine;

public enum Speaker
{
    Player,
    CharacterA,
    Narrator,
    Leyla
}

public enum Expression
{
    Neutral,
    Happy,
    Angry,
    Sad
}

[Serializable]
public class Dialogue
{
    public Speaker speaker;

    [TextArea(2, 5)]
    public string text;
    public bool showPortrait = true;
    public Expression expression;
}
