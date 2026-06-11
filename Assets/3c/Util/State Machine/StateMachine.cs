using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class StateMachine<T>
{
    public State<T> CurrentState {  get; private set; }

    T _owner;
    public StateMachine(T owner)
    {
        _owner = owner;
    }
    public void ChangeState(State<T> newstate)
    {
        CurrentState?.Exit();
        CurrentState = newstate;
        if (CurrentState != null)
            CurrentState.Enter(_owner);
        else
            Debug.LogError("StateMachine: ³¢ÊÔÇÐ»»µ½¿Õ×´Ì¬£¡");
    }
    public void Execute()
    {
        CurrentState?.Execute();
    }
}
