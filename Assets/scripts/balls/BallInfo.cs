using UnityEngine;

public class BallInfo : MonoBehaviour
{
    public enum BallType
    {
        CueBall,    
        Solid,      
        Striped,    
        EightBall   
    }

    [Header("Тип шара")]
    public BallType type;

    [Tooltip("Номер шара")]
    public int number;
}