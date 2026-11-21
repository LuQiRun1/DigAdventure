using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ItemQuality = GameDataConfig.ItemQuality;

[RequireComponent(typeof(Animator))]
public class AdventurerView : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float moveSpeed = 4f;
    [SerializeField, Min(0.05f)] private float holdDelay = 0.2f;
    [SerializeField] private ItemQuality equippedShovelQuality;
    [SerializeField, Tooltip("填写信息编辑器里的铲子编号。留空时使用对应品质的默认铲子。")]
    private string equippedShovelId;
    private ChestCatalog catalog;
    private ChestCatalog.Shovel shovel;
    private ShovelProgress shovelProgress;
    private bool scanning;
    private bool digging;
    private bool continueAutomaticDig;
    private bool manualSwingCompletesLayer;
    private int queuedManualSwings;
    private float actionEndsAt;
    private readonly List<RaycastResult> pointerHits = new List<RaycastResult>();
    public bool HasShovel => shovelProgress != null && isActiveAndEnabled;
    public bool UsesManualDig => HasShovel && shovel.Mode == ChestCatalog.DigMode.Manual;
    public float ScanSecondsRemaining => !HasShovel ? 0f : scanning ? ActionSecondsRemaining : shovel.ScanSeconds;
    public float DigSecondsRemaining => !HasShovel ? 0f : digging ? ActionSecondsRemaining : shovel.DigSecondsPerLayer;
    public int DigClicksRemaining => !HasShovel || (digging && manualSwingCompletesLayer)
        ? 0 : shovel.ClicksPerLayer - shovelProgress.GetClicks(cell);
    private float ActionSecondsRemaining => Mathf.Max(0f, actionEndsAt - Time.time);
    private List<ShovelProgress.Detection> latestScan;
    public IReadOnlyList<ShovelProgress.Detection> LatestScan =>
        latestScan ?? (IReadOnlyList<ShovelProgress.Detection>)System.Array.Empty<ShovelProgress.Detection>();
    public ItemQuality? LastDugChest { get; private set; }
    private float pressedAt;
    private bool trackingPress;
    private bool following;
    private Vector2Int? pointerCell;

    private static readonly Vector2Int[] Directions =
        { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down };

    private readonly Queue<Vector2Int> path = new Queue<Vector2Int>();
    private LevelData level;
    private DigAdventureGameBoard board;
    private Camera inputCamera;
    private Animator animator;
    private Vector2 gridOrigin;
    private Vector2Int cell;
    private Vector2Int stepCell;
    private float cellSize = 1f;
    private bool movingToStep;
    public bool IsMoving => movingToStep;
    public struct ScentMark
    {
        public Vector2Int cell;
        public float time;
    }
    public const float ScentLifetime = 3f;
    private readonly List<ScentMark> scentTrail = new List<ScentMark>();
    public IReadOnlyList<ScentMark> ScentTrail => scentTrail;

    private void LeaveScent(Vector2Int position)
    {
        float now = Time.time;
        int last = scentTrail.Count - 1;
        var mark = new ScentMark { cell = position, time = now };
        if (last >= 0 && scentTrail[last].cell == position)
            scentTrail[last] = mark;
        else
            scentTrail.Add(mark);
        while (scentTrail.Count > 0 && now - scentTrail[0].time >= ScentLifetime)
            scentTrail.RemoveAt(0);
    }
    private Vector2Int? moveTarget;
    private bool digOnArrival;
    private Vector2Int facing = Vector2Int.down;
    private Vector2Int animatedFacing;
    private string animatedAction;
    private int animatedState;

    public void Initialize(LevelData boardLevel, Vector2 worldGridOrigin, float worldCellSize = 1f,
        DigAdventureGameBoard gameBoard = null)
    {
        level = boardLevel;
        board = gameBoard;
        gridOrigin = worldGridOrigin;
        cellSize = worldCellSize;
        inputCamera = Camera.main;
    }

    private void Start()
    {
        animator = GetComponent<Animator>();
        if (inputCamera == null)
            inputCamera = Camera.main;

        cell = WorldToCell(transform.position);
        if (level == null || !level.IsCellWalkable(cell))
        {
            Debug.LogWarning("Adventurer 需要有效关卡，且出生位置必须位于可通行格内。", this);
            enabled = false;
            return;
        }

        transform.position = CellCenter(cell);
        catalog = Resources.Load<ChestCatalog>("ChestCatalog");
        shovel = catalog ? catalog.GetShovel(equippedShovelId) ?? catalog.GetShovel(equippedShovelQuality)
            : new ChestCatalog.Shovel();
        shovelProgress = new ShovelProgress(level);
        LeaveScent(cell);
        PlayAnimation("Idle");
    }

    private void Update()
    {
        if (scanning && Time.time >= actionEndsAt)
        {
            StopScan();
            latestScan = shovelProgress.Scan(cell, shovel);
        }
        if (digging) UpdateDigging();

        UpdatePointerMovement();
        LeaveScent(WorldToCell(transform.position));

        if (Input.GetKeyDown(KeyCode.Q))
            TryStartScan();

        if (digging || (scanning && (!digOnArrival || moveTarget != cell || trackingPress))) return;

        MoveAlongPath();
        if (movingToStep || path.Count > 0)
        {
            PlayAnimation("Move");
            return;
        }

        // 只在真正到达点击格时挖掘，最近可达格仍然只是移动的备选终点。
        bool shouldDig = digOnArrival && moveTarget == cell;
        if (shouldDig && trackingPress && !following)
        {
            PlayAnimation("Idle");
            return;
        }
        moveTarget = null;
        digOnArrival = false;
        if (shouldDig && TryStartDig())
            return;
        PlayAnimation("Idle");
    }

    public bool TryEquipShovel(ItemQuality quality)
    {
        if (!catalog || (uint)quality > (uint)ItemQuality.Red)
            return false;
        return TryEquipShovel(catalog.GetShovel(quality));
    }

    public bool TryEquipShovel(string id) => catalog && TryEquipShovel(catalog.GetShovel(id));

    private bool TryEquipShovel(ChestCatalog.Shovel item)
    {
        if (item == null || !HasShovel) return false;
        if (ReferenceEquals(shovel, item)) return true;

        CancelShovelAction();
        equippedShovelQuality = item.Quality;
        equippedShovelId = item.Id;
        shovel = item;
        latestScan = null;
        return true;
    }

    public bool TryStartScan()
    {
        if (!CanUseShovel() || scanning)
            return false;

        CancelShovelAction();
        scanning = true;
        actionEndsAt = Time.time + shovel.ScanSeconds;
        if (board) board.ShowScanArea(cell, shovel.ScanRadius);
        return true;
    }

    private bool CanUseShovel() => HasShovel && !movingToStep && path.Count == 0;

    public void CancelShovelAction()
    {
        if (shovelProgress == null) return;

        StopScan();
        digging = false;
        continueAutomaticDig = false;
        manualSwingCompletesLayer = false;
        queuedManualSwings = 0;
        actionEndsAt = 0f;
        digOnArrival = false;
        shovelProgress.ClearClicks();
        if (animatedAction == "Dig") PlayAnimation("Idle");
    }

    private void OnDisable() => StopScan();

    private void StopScan()
    {
        if (!scanning) return;

        scanning = false;
        if (board) board.ClearScanArea();
    }

    public bool TryDigCurrentCell()
    {
        if (!CanUseShovel()) return false;
        if (!digging) return TryStartDig(continuous: true);

        if (UsesManualDig)
        {
            if (queuedManualSwings == int.MaxValue) return false;
            queuedManualSwings++;
        }
        else
        {
            CancelShovelAction();
        }
        return true;
    }

    private bool TryStartDig(bool continuous = false)
    {
        if (!shovelProgress.CanDig(cell))
            return false;

        if (scanning) CancelShovelAction();
        digging = true;
        if (UsesManualDig)
        {
            StartManualSwing();
        }
        else
        {
            continueAutomaticDig = continuous;
            actionEndsAt = Time.time + shovel.DigSecondsPerLayer;
            PlayAnimation("Dig");
        }
        return true;
    }

    private void StartManualSwing()
    {
        manualSwingCompletesLayer = shovelProgress.RegisterClick(cell, shovel.ClicksPerLayer);
        PlayAnimation("Dig");
    }

    private void UpdateDigging()
    {
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        bool hasDigAnimation = animator.HasState(0, animatedState);
        bool swingFinished = state.fullPathHash == animatedState && state.normalizedTime >= 1f;
        if (UsesManualDig)
        {
            if (hasDigAnimation && !swingFinished) return;

            if (manualSwingCompletesLayer)
                LastDugChest = shovelProgress.DigLayer(cell);

            if (queuedManualSwings > 0 && shovelProgress.CanDig(cell))
            {
                queuedManualSwings--;
                StartManualSwing();
                return;
            }
            queuedManualSwings = 0;
            manualSwingCompletesLayer = false;
        }
        else
        {
            if (Time.time >= actionEndsAt)
            {
                LastDugChest = shovelProgress.DigLayer(cell);
                if (!continueAutomaticDig || !shovelProgress.CanDig(cell))
                {
                    digging = false;
                    return;
                }
                actionEndsAt = Time.time + shovel.DigSecondsPerLayer;
            }

            if (hasDigAnimation && swingFinished && !state.loop)
                PlayAnimation("Dig");
            return;
        }

        digging = false;
    }

    private void UpdatePointerMovement()
    {
        if (Input.GetMouseButtonDown(0))
        {
            if (IsPointerOverButton(Input.mousePosition)) return;
            trackingPress = true;
            following = false;
            pressedAt = Time.unscaledTime;
            pointerCell = null;
            HandlePointerCell(Input.mousePosition, true);
        }
        else if (trackingPress && Input.GetMouseButton(0))
        {
            if (Time.unscaledTime - pressedAt < holdDelay) return;
            following = true;
            digOnArrival = false;
            HandlePointerCell(Input.mousePosition, false);
        }
        else if (trackingPress)
        {
            if (following)
            {
                path.Clear();
                moveTarget = null;
            }
            trackingPress = false;
            following = false;
        }
    }

    private bool IsPointerOverButton(Vector2 position)
    {
        var eventSystem = EventSystem.current;
        if (!eventSystem) return false;

        pointerHits.Clear();
        eventSystem.RaycastAll(new PointerEventData(eventSystem) { position = position }, pointerHits);
        return pointerHits.Count > 0 && pointerHits[0].gameObject.GetComponentInParent<Button>() != null;
    }

    private void HandlePointerCell(Vector3 screenPosition, bool click)
    {
        if (inputCamera == null)
            return;

        var plane = new Plane(Vector3.forward, transform.position);
        Ray ray = inputCamera.ScreenPointToRay(screenPosition);
        if (!plane.Raycast(ray, out float distance))
            return;

        Vector2Int requestedCell = WorldToCell(ray.GetPoint(distance));
        if (!level.ContainsCell(requestedCell))
        {
            if (!click && pointerCell.HasValue)
            {
                path.Clear();
                moveTarget = null;
                pointerCell = null;
            }
            return;
        }

        if (!click && pointerCell == requestedCell) return;
        pointerCell = requestedCell;

        if (moveTarget == requestedCell)
        {
            digOnArrival = click;
            return;
        }

        moveTarget = requestedCell;
        digOnArrival = click && requestedCell == WorldToCell(transform.position);

        if (digOnArrival && movingToStep && requestedCell == cell)
        {
            // 还在出发格内，沿当前线段返回格中心后挖掘。
            cell = stepCell;
            stepCell = requestedCell;
            facing = stepCell - cell;
            path.Clear();
            return;
        }

        // 改变目的地时先走完当前格，避免角色在两格之间突然转向并斜穿障碍。
        Vector2Int start = movingToStep ? stepCell : cell;
        List<Vector2Int> newPath = FindPathToClosestReachable(start, requestedCell);
        if (newPath.Count > 0)
            CancelShovelAction();
        path.Clear();
        for (int i = 0; i < newPath.Count; i++)
            path.Enqueue(newPath[i]);
    }

    private List<Vector2Int> FindPathToClosestReachable(Vector2Int start, Vector2Int requested)
    {
        var frontier = new Queue<Vector2Int>();
        var parent = new Dictionary<Vector2Int, Vector2Int>();
        var distanceFromStart = new Dictionary<Vector2Int, int>();
        frontier.Enqueue(start);
        parent[start] = start;
        distanceFromStart[start] = 0;

        Vector2Int best = start;
        int bestTargetDistance = SquaredDistance(start, requested);
        int bestPathDistance = 0;

        while (frontier.Count > 0)
        {
            Vector2Int current = frontier.Dequeue();
            int targetDistance = SquaredDistance(current, requested);
            int pathDistance = distanceFromStart[current];
            if (targetDistance < bestTargetDistance ||
                (targetDistance == bestTargetDistance && pathDistance < bestPathDistance))
            {
                best = current;
                bestTargetDistance = targetDistance;
                bestPathDistance = pathDistance;
            }

            if (current == requested)
            {
                best = current;
                break;
            }

            for (int i = 0; i < Directions.Length; i++)
            {
                Vector2Int next = current + Directions[i];
                if (parent.ContainsKey(next) || !level.IsCellWalkable(next))
                    continue;

                parent[next] = current;
                distanceFromStart[next] = pathDistance + 1;
                frontier.Enqueue(next);
            }
        }

        var result = new List<Vector2Int>();
        while (best != start)
        {
            result.Add(best);
            best = parent[best];
        }
        result.Reverse();
        return result;
    }

    private void MoveAlongPath()
    {
        float remainingMovement = moveSpeed * cellSize * Time.deltaTime;
        while (remainingMovement > 0f)
        {
            if (!movingToStep)
            {
                if (path.Count == 0)
                    return;

                stepCell = path.Dequeue();
                movingToStep = true;
                facing = stepCell - cell;
            }

            Vector3 destination = CellCenter(stepCell);
            float distance = Vector3.Distance(transform.position, destination);
            transform.position = Vector3.MoveTowards(transform.position, destination, remainingMovement);
            LeaveScent(WorldToCell(transform.position));
            if (remainingMovement < distance)
                return;

            remainingMovement -= distance;
            cell = stepCell;
            movingToStep = false;
        }
    }

    private void PlayAnimation(string action)
    {
        if (action != "Dig" && facing == animatedFacing && action == animatedAction)
            return;

        animatedFacing = facing;
        animatedAction = action;

        string direction;
        if (facing.x < 0) direction = "L";
        else if (facing.x > 0) direction = "R";
        // 关卡行号向下递增。
        else if (facing.y > 0) direction = "D";
        else direction = "T";

        animatedState = Animator.StringToHash($"Base Layer.Adventurer{action}-{direction}");
        animator.Play(animatedState, 0, 0f);
    }

    private Vector2Int WorldToCell(Vector3 position)
    {
        return new Vector2Int(Mathf.RoundToInt((position.x - gridOrigin.x) / cellSize),
            Mathf.RoundToInt((gridOrigin.y - position.y) / cellSize));
    }

    private Vector3 CellCenter(Vector2Int coordinate)
    {
        return new Vector3(gridOrigin.x + coordinate.x * cellSize,
            gridOrigin.y - coordinate.y * cellSize, transform.position.z);
    }

    private static int SquaredDistance(Vector2Int a, Vector2Int b)
    {
        int x = a.x - b.x;
        int y = a.y - b.y;
        return x * x + y * y;
    }
}
