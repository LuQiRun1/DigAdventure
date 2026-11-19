using UnityEngine;

[RequireComponent(typeof(Animator))]
public class SlimeView : MonoBehaviour
{
    [SerializeField] private LevelData level;
    [Tooltip("关卡左上角格子中心的世界坐标，每格为一个世界单位。")]
    [SerializeField] private Vector2 gridOrigin;

    private static readonly Vector2Int[] Directions =
        { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down };
    private Vector2Int cell;
    private Vector2Int target;
    private Vector2Int lastDirection;
    private Animator animator;
    private float cellSize = 1f;

    public void Initialize(LevelData boardLevel, Vector2 worldGridOrigin, float worldCellSize = 1f)
    {
        level = boardLevel;
        gridOrigin = worldGridOrigin;
        cellSize = worldCellSize;
    }

    private void Start()
    {
        animator = GetComponent<Animator>();
        Vector3 position = transform.position;
        cell = new Vector2Int(Mathf.RoundToInt((position.x - gridOrigin.x) / cellSize),
            Mathf.RoundToInt((gridOrigin.y - position.y) / cellSize));
        if (level == null || !level.IsCellWalkable(cell))
        {
            Debug.LogWarning("Slime 需要有效关卡，且出生位置必须位于可通行格内。", this);
            enabled = false;
            return;
        }
        target = cell;
        transform.position = CellCenter(cell);
    }

    private Vector3 CellCenter(Vector2Int coordinate)
    {
        return new Vector3(gridOrigin.x + coordinate.x * cellSize, gridOrigin.y - coordinate.y * cellSize, transform.position.z);
    }

    private void Update()
    {
        float distance = Time.deltaTime * cellSize; // 每秒一格。
        while (distance > 0f)
        {
            if (cell == target)
            {
                if (!ChooseTarget())
                {
                    animator.speed = 0f;
                    return;
                }
                animator.speed = 1f;
                Vector2Int direction = target - cell;
                if (direction != lastDirection)
                    PlayMoveAnimation(direction);
            }
            Vector3 destination = CellCenter(target);
            float remaining = Vector3.Distance(transform.position, destination);
            transform.position = Vector3.MoveTowards(transform.position, destination, distance);
            if (distance < remaining) return;
            distance -= remaining;
            lastDirection = target - cell;
            cell = target;
        }
    }

    private void PlayMoveAnimation(Vector2Int direction)
    {
        string state;
        if (direction.x < 0) state = "Base Layer.SlimeMove-L";
        else if (direction.x > 0) state = "Base Layer.SlimeMove-R";
        // 关卡行号向下递增。
        else if (direction.y > 0) state = "Base Layer.SlimeMove-D";
        else state = "Base Layer.SlimeMove-T";
        animator.Play(state, 0, 0f);
    }

    private bool ChooseTarget()
    {
        int choices = 0;
        foreach (Vector2Int direction in Directions)
        {
            Vector2Int next = cell + direction;
            if (direction == -lastDirection || !level.IsCellWalkable(next)) continue;
            if (Random.Range(0, ++choices) == 0) target = next;
        }
        if (choices > 0) return true;
        Vector2Int back = cell - lastDirection;
        if (back == cell || !level.IsCellWalkable(back)) return false;
        target = back;
        return true;
    }
}
