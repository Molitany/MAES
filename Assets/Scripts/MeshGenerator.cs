using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class MeshGenerator : MonoBehaviour {
	/**
	 * Uses the marching squares algorithm to smooth out
	 * the grid and create a continuous wall around the rooms 
	 */
	public SquareGrid squareGrid;
	// The inner walls, that the robots can collide with
	public MeshFilter innerWalls;
	// The outer/upper parts of the closed off cave
	public MeshFilter wallRoof;

	private List<Vector3> vertices = new List<Vector3>();
	// list of all vertices in triangles
	// This list tells unity in which order to read the vertices.
	private List<int> triangles = new List<int>();
	
	// Map from vertex index to all triangles containing the vertex.
	private Dictionary<int,List<Triangle>> triangleDictionary = new Dictionary<int, List<Triangle>> ();
	// A list of all outlines containing lists of vertex indexes contained in the given outline.
	// An outline is a wall either around an island of walls inside a room
	// or the walls around a room.
	private List<List<int>> outlines = new List<List<int>> ();
	// Used to avoid checking the same case twice
	private HashSet<int> checkedVertices = new HashSet<int>();
	
	private const int WALL_TYPE = 1, ROOM_TYPE = 0;

	public void ClearMesh(){
		squareGrid = null;
		Destroy(innerWalls.gameObject.GetComponent<MeshCollider>());
		innerWalls.mesh.Clear();
		wallRoof.mesh.Clear();
		vertices.Clear();
		triangles.Clear();
		triangleDictionary.Clear ();
		outlines.Clear();
		checkedVertices.Clear();
	}

	public void GenerateMesh(int[,] map, float squareSize, float wallHeight) {
		// Generate grid of squares containing control nodes and between nodes 
		// for the marching square algorithm
		squareGrid = new SquareGrid(map, squareSize);

		vertices = new List<Vector3>();
		triangles = new List<int>();
		
		for (int x = 0; x < squareGrid.squares.GetLength(0); x ++) {
			for (int y = 0; y < squareGrid.squares.GetLength(1); y ++) {
				// Create triangles from all the points in the squares
				// assigned to variables "vertices" and "triangles"
				TriangulateSquare(squareGrid.squares[x,y]);
			}
		}

		// Create roof mesh
		Mesh wallRoofMesh = new Mesh();
		wallRoofMesh.vertices = vertices.ToArray();
		wallRoofMesh.triangles = triangles.ToArray();
		wallRoofMesh.RecalculateNormals();
		
		// Apply mesh to wall roof
		wallRoof.mesh = wallRoofMesh;

		// This bit of code allows us to apply a texture on the mesh
		// Tiling refers to using the same texture next to each other.
		/*int tileAmount = 10;
		Vector2[] uvs = new Vector2[vertices.Count];
		for (int i = 0; i < vertices.Count; i ++) {
			float percentX = Mathf.InverseLerp(-map.GetLength(0) / 2 * squareSize,map.GetLength(0) / 2 * squareSize,vertices[i].x) * tileAmount;
			float percentY = Mathf.InverseLerp(-map.GetLength(0) / 2 * squareSize,map.GetLength(0) / 2 * squareSize,vertices[i].z) * tileAmount;
			uvs[i] = new Vector2(percentX, percentY);
		}
		wallRoofMesh.uv = uvs;*/
		

		CreateWallMesh(wallHeight);
	}

	void CreateWallMesh(float wallHeight) {
		// Assigns outline vertices to list variable "outlines"
		// An outline is a wall either around an island of walls inside a room
		// or the walls around a room.
		CalculateMeshOutlines();

		List<Vector3> wallVertices = new List<Vector3>();
		List<int> wallTriangles = new List<int>();
		Mesh innerWallsMesh = new Mesh();

		foreach(List<int> outline in outlines) {
			for(int i = 0; i < outline.Count - 1; i++) {
				int startIndex = wallVertices.Count;
				// Create section of the wall currently being made
				// as viewed from inside the room looked at the wall
				wallVertices.Add(vertices[outline[i]]); // top left (0)
				wallVertices.Add(vertices[outline[i + 1]]); // top right (1)
				wallVertices.Add(vertices[outline[i]] - Vector3.up * wallHeight); // bottom left (2)
				wallVertices.Add(vertices[outline[i+1]] - Vector3.up * wallHeight); // bottom right (3)

				// Adding counter clockwise
				// the number 0 to 3 comes from the order in which they were 
				// added to the wall vertices.
				
				// Triangle one (left side lower side of the wall square)
				wallTriangles.Add(startIndex + 0); // Top left
				wallTriangles.Add(startIndex + 2); // Bottom left
				wallTriangles.Add(startIndex + 3); // Bottom right
				
				// Triangle two (right side upper side of the wall square)
				wallTriangles.Add(startIndex + 3); // Bottom Right
				wallTriangles.Add(startIndex + 1); // Top right
				wallTriangles.Add(startIndex + 0); // Top left
			}
		}
		// Unity cannot work with lists, so ToArray() is needed
		innerWallsMesh.vertices = wallVertices.ToArray();
		innerWallsMesh.triangles = wallTriangles.ToArray();
		innerWalls.mesh = innerWallsMesh;

		MeshCollider wallCollider = innerWalls.gameObject.AddComponent<MeshCollider> ();
		wallCollider.sharedMesh = innerWallsMesh;
	}

	// According to the marching squares algorithm,
	// there are 16 cases, since there are 16 combinations of ON
	// and OFF for a box where each of the 4 corners can have either of these 
	// states.
	void TriangulateSquare(Square square) {
		switch (square.configuration) {
		case 0:
			break;

		// 1 points:
		case 1:
			MeshFromPoints(square.centreLeft, square.centreBottom, square.bottomLeft);
			break;
		case 2:
			MeshFromPoints(square.bottomRight, square.centreBottom, square.centreRight);
			break;
		case 4:
			MeshFromPoints(square.topRight, square.centreRight, square.centreTop);
			break;
		case 8:
			MeshFromPoints(square.topLeft, square.centreTop, square.centreLeft);
			break;

		// 2 points:
		case 3:
			MeshFromPoints(square.centreRight, square.bottomRight, square.bottomLeft, square.centreLeft);
			break;
		case 6:
			MeshFromPoints(square.centreTop, square.topRight, square.bottomRight, square.centreBottom);
			break;
		case 9:
			MeshFromPoints(square.topLeft, square.centreTop, square.centreBottom, square.bottomLeft);
			break;
		case 12:
			MeshFromPoints(square.topLeft, square.topRight, square.centreRight, square.centreLeft);
			break;
		case 5:
			MeshFromPoints(square.centreTop, square.topRight, square.centreRight, square.centreBottom, square.bottomLeft, square.centreLeft);
			break;
		case 10:
			MeshFromPoints(square.topLeft, square.centreTop, square.centreRight, square.bottomRight, square.centreBottom, square.centreLeft);
			break;

		// 3 point:
		case 7:
			MeshFromPoints(square.centreTop, square.topRight, square.bottomRight, square.bottomLeft, square.centreLeft);
			break;
		case 11:
			MeshFromPoints(square.topLeft, square.centreTop, square.centreRight, square.bottomRight, square.bottomLeft);
			break;
		case 13:
			MeshFromPoints(square.topLeft, square.topRight, square.centreRight, square.centreBottom, square.bottomLeft);
			break;
		case 14:
			MeshFromPoints(square.topLeft, square.topRight, square.bottomRight, square.centreBottom, square.centreLeft);
			break;

		// 4 point:
		case 15:
			MeshFromPoints(square.topLeft, square.topRight, square.bottomRight, square.bottomLeft);
			// If all 4 are active walls, it cannot be an outline to a room, and should thus not be checked
			checkedVertices.Add(square.topLeft.vertexIndex);
			checkedVertices.Add(square.topRight.vertexIndex);
			checkedVertices.Add(square.bottomRight.vertexIndex);
			checkedVertices.Add(square.bottomLeft.vertexIndex);
			break;
		}

	}

	void MeshFromPoints(params Node[] points) {
		AssignIndexesToVertices(points);

		if (points.Length >= 3)
			CreateTriangle(points[0], points[1], points[2]);
		if (points.Length >= 4)
			CreateTriangle(points[0], points[2], points[3]);
		if (points.Length >= 5) 
			CreateTriangle(points[0], points[3], points[4]);
		if (points.Length >= 6)
			CreateTriangle(points[0], points[4], points[5]);

	}

	void AssignIndexesToVertices(Node[] points) {
		for (int i = 0; i < points.Length; i ++) {
			if (points[i].vertexIndex == -1) {
				points[i].vertexIndex = vertices.Count;
				vertices.Add(points[i].position);
			}
		}
	}

	void CreateTriangle(Node a, Node b, Node c) {
		triangles.Add(a.vertexIndex);
		triangles.Add(b.vertexIndex);
		triangles.Add(c.vertexIndex);

		Triangle triangle = new Triangle(a.vertexIndex, b.vertexIndex, c.vertexIndex);
		AddTriangleToDictionary(triangle.vertexIndexA, triangle);
		AddTriangleToDictionary(triangle.vertexIndexB, triangle);
		AddTriangleToDictionary(triangle.vertexIndexC, triangle);
	}

	void AddTriangleToDictionary(int vertexIndexKey, Triangle triangle) {
		if (triangleDictionary.ContainsKey(vertexIndexKey)) {
			triangleDictionary[vertexIndexKey].Add(triangle);
		} else {
			List<Triangle> triangleList = new List<Triangle>();
			triangleList.Add(triangle);
			triangleDictionary.Add(vertexIndexKey, triangleList);
		}
	}

	void CalculateMeshOutlines() {
		for (int vertexIndex = 0; vertexIndex < vertices.Count; vertexIndex ++) {
			if (!checkedVertices.Contains(vertexIndex)) {
				int newOutlineVertex = GetConnectedOutlineVertex(vertexIndex);
				if (newOutlineVertex != -1) {
					checkedVertices.Add(vertexIndex);

					List<int> newOutline = new List<int>();
					newOutline.Add(vertexIndex);
					outlines.Add(newOutline);
					FollowOutline(newOutlineVertex, outlines.Count-1);
					outlines[outlines.Count-1].Add(vertexIndex);
				}
			}
		}
	}

	void FollowOutline(int vertexIndex, int outlineIndex) {
		outlines[outlineIndex].Add(vertexIndex);
		checkedVertices.Add(vertexIndex);
		int nextVertexIndex = GetConnectedOutlineVertex(vertexIndex);

		if (nextVertexIndex != -1) {
			FollowOutline(nextVertexIndex, outlineIndex);
		}
	}

	int GetConnectedOutlineVertex(int vertexIndex) {
		List<Triangle> trianglesContainingVertex = triangleDictionary[vertexIndex];

		for (int i = 0; i < trianglesContainingVertex.Count; i ++) {
			Triangle triangle = trianglesContainingVertex[i];

			for (int j = 0; j < 3; j ++) {
				int vertexB = triangle[j];
				if (vertexB != vertexIndex && !checkedVertices.Contains(vertexB)) {
					if (IsOutlineEdge(vertexIndex, vertexB)) {
						return vertexB;
					}
				}
			}
		}

		return -1;
	}

	bool IsOutlineEdge(int vertexA, int vertexB) {
		// The inner walls made up of triangles are recognized based on the
		// number of triangles shared between the two vertices. The outer ones only have 1 in common.
		List<Triangle> trianglesContainingVertexA = triangleDictionary[vertexA];
		int sharedTriangleCount = 0;

		for (int i = 0; i < trianglesContainingVertexA.Count; i ++) {
			if (trianglesContainingVertexA[i].Contains(vertexB)) {
				sharedTriangleCount ++;
				if (sharedTriangleCount > 1) {
					break;
				}
			}
		}
		return sharedTriangleCount == 1;
	}

	struct Triangle {
		public int vertexIndexA;
		public int vertexIndexB;
		public int vertexIndexC;
		int[] vertices;

		public Triangle (int a, int b, int c) {
			vertexIndexA = a;
			vertexIndexB = b;
			vertexIndexC = c;

			vertices = new int[3];
			vertices[0] = a;
			vertices[1] = b;
			vertices[2] = c;
		}

		public int this[int i] {
			get {
				return vertices[i];
			}
		}


		public bool Contains(int vertexIndex) {
			return vertexIndex == vertexIndexA || vertexIndex == vertexIndexB || vertexIndex == vertexIndexC;
		}
	}

	public class SquareGrid {
		public Square[,] squares;

		public SquareGrid(int[,] map, float squareSize) {
			int nodeCountX = map.GetLength(0);
			int nodeCountY = map.GetLength(1);
			float mapWidth = nodeCountX * squareSize;
			float mapHeight = nodeCountY * squareSize;

			// Create map of control nodes
			ControlNode[,] controlNodes = new ControlNode[nodeCountX, nodeCountY];

			for (int x = 0; x < nodeCountX; x ++) {
				for (int y = 0; y < nodeCountY; y ++) {
					// Divided by 2, since we start in 0,0 and can go both above and below 0.
					Vector3 position = new Vector3(-mapWidth/2 + x * squareSize + squareSize/2, 0, -mapHeight / 2 + y * squareSize + squareSize / 2);
					controlNodes[x,y] = new ControlNode(position,map[x,y] == WALL_TYPE, squareSize);
				}
			}

			squares = new Square[nodeCountX - 1,nodeCountY - 1];
			for (int x = 0; x < nodeCountX - 1; x++) {
				for (int y = 0; y < nodeCountY - 1; y++) {
					squares[x,y] = new Square(controlNodes[x,y + 1], controlNodes[x + 1,y + 1], controlNodes[x + 1,y], controlNodes[x,y]);
				}
			}

		}
	}
	
	public class Square {

		// This class is used in the marching squares algorithm.
		// Control nodes can be either on or off
		public ControlNode topLeft, topRight, bottomRight, bottomLeft;
		public Node centreTop, centreRight, centreBottom, centreLeft;
		public int configuration;

		public Square (ControlNode topLeft, ControlNode topRight, ControlNode bottomRight, ControlNode bottomLeft) {
			this.topLeft = topLeft;
			this.topRight = topRight;
			this.bottomRight = bottomRight;
			this.bottomLeft = bottomLeft;

			// Assign references
			centreTop = this.topLeft.right;
			centreRight = this.bottomRight.above;
			centreBottom = this.bottomLeft.right;
			centreLeft = this.bottomLeft.above;

			// There are only 16 possible configurations
			// Consider them in binary xxxx
			// First bit is bottomLeft, second is bottomRight etc.
			// this way we can deduct the configuration from the active nodes
			// like below.
			if (this.topLeft.isWall)
				configuration += 8;
			if (this.topRight.isWall)
				configuration += 4;
			if (this.bottomRight.isWall)
				configuration += 2;
			if (this.bottomLeft.isWall)
				configuration += 1;
		}

	}
	
	public class Node {
		public Vector3 position;
		public int vertexIndex = -1;

		public Node(Vector3 position) {
			this.position = position;
		}
	}

	public class ControlNode : Node {

		public bool isWall;
		public Node above, right;

		public ControlNode(Vector3 position, bool isWall, float squareSize) : base(position) {
			this.isWall = isWall;
			above = new Node(base.position + Vector3.forward * squareSize / 2f);
			right = new Node(base.position + Vector3.right * squareSize / 2f);
		}

	}
}