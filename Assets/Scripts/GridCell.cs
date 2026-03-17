using UnityEngine;

public class GridCell : MonoBehaviour
{
    public int gridX;
    public int gridY;

    LaserDetector detector;

    Mesh mesh;

    public void Init(int x, int y, LaserDetector d)
    {
        gridX = x;
        gridY = y;
        detector = d;

        mesh = new Mesh();

        GetComponent<MeshFilter>().mesh = mesh;
    }

    public void UpdateShape(Vector2 p00, Vector2 p10, Vector2 p01, Vector2 p11)
    {
        Vector3[] v = new Vector3[4];

        v[0] = p00;
        v[1] = p10;
        v[2] = p01;
        v[3] = p11;

        int[] t = new int[]
        {
            0,2,1,
            2,3,1
        };

        mesh.vertices = v;
        mesh.triangles = t;
    }

    public void OnLaserHit(float brightness)
    {
        Debug.Log($"Laser Hit Grid ({gridX},{gridY}) brightness:{brightness}");
    }
}