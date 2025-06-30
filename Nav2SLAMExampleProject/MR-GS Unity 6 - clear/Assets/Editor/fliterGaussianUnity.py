import numpy as np
from plyfile import PlyData, PlyElement
from sklearn.neighbors import BallTree
import sys
import os

def filter_sparse_points(points, center, max_radius, density_threshold=0.1):
    ball_tree = BallTree(points)
    chunk_size = 10000
    sparse_mask = np.zeros(len(points), dtype=bool)

    for i in range(0, len(points), chunk_size):
        chunk = points[i:i+chunk_size]
        counts = ball_tree.query_radius(chunk, r=max_radius, count_only=True)
        sparse_mask[i:i+chunk_size] = counts < density_threshold * (4/3 * np.pi * max_radius**3)

    return ~sparse_mask

if __name__ == "__main__":
    if len(sys.argv) != 3:
        print("Usage: python filter_3dgs.py <input.ply> <output.ply>")
        sys.exit(1)

    input_path = sys.argv[1]
    output_path = sys.argv[2]

    print(f"[INFO] Reading from: {input_path}")
    ply_data = PlyData.read(input_path)
    vertices = ply_data["vertex"].data
    points = np.vstack([vertices["x"], vertices["y"], vertices["z"]]).T

    center = np.median(points, axis=0)
    distances = np.linalg.norm(points - center, axis=1)
    max_radius = np.median(distances) + 3 * np.std(distances)

    dense_mask = filter_sparse_points(points, center, max_radius)
    filtered_vertices = vertices[dense_mask]

    filtered_element = PlyElement.describe(filtered_vertices, "vertex")
    PlyData([filtered_element], text=False).write(output_path)

    print(f"[SUCCESS] Filtered output saved to: {output_path}")
