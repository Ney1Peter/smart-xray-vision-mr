using KNN;
using KNN.Jobs;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.VFX;

public class KnnVFXVisualization : MonoBehaviour
{
    public enum QueryMode
    {
        KNearest,
        Range
    }

    public QueryMode Mode;
    public int ParticleCount = 20000;
    public int QueryK = 20;
    public float QueryRange = 1.0f;

    public VisualEffect visualEffect;
    ParticleSystem m_system;

    NativeArray<float3> m_queryPositions;
    NativeArray<Color32> m_queryColors;
    NativeArray<float3> m_points;
    NativeArray<int> m_results;
    NativeArray<RangeQueryResult> m_rangeResults;

    KnnContainer m_container;
    GraphicsBuffer positionBuffer;
    GraphicsBuffer colorBuffer;
    ParticleSystem.Particle[] particles;

    void Start()
    {
        m_system = GetComponent<ParticleSystem>();
        m_system.Emit(ParticleCount);

        m_points = new NativeArray<float3>(ParticleCount, Allocator.Persistent);
        particles = new ParticleSystem.Particle[ParticleCount];

        positionBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, ParticleCount, sizeof(float) * 3);
        colorBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, ParticleCount, sizeof(float) * 4);

        m_container = new KnnContainer(m_points, false, Allocator.Persistent);
    }

    void OnDestroy()
    {
        m_points.Dispose();
        m_results.Dispose();
        m_queryPositions.Dispose();
        m_queryColors.Dispose();
        foreach (var result in m_rangeResults) result.Dispose();
        m_rangeResults.Dispose();

        positionBuffer.Dispose();
        colorBuffer.Dispose();
    }

    void Update()
    {
        m_system.GetParticles(particles);

        for (int i = 0; i < particles.Length; i++)
        {
            m_points[i] = particles[i].position;
        }

        Color32[] colors = new Color32[ParticleCount];
        for (int i = 0; i < ParticleCount; i++)
        {
            colors[i] = new Color32(0, 0, 0, 255);
        }

        if (Mode == QueryMode.KNearest)
        {
            for (int i = 0; i < m_results.Length; i++)
            {
                int index = m_results[i];
                if (index >= 0 && index < ParticleCount)
                    colors[index] = m_queryColors[i / QueryK];
            }
        }
        else
        {
            for (int i = 0; i < m_rangeResults.Length; i++)
            {
                var result = m_rangeResults[i];
                var color = m_queryColors[i];
                for (int j = 0; j < result.Length; j++)
                {
                    int idx = result[j];
                    if (idx >= 0 && idx < ParticleCount)
                        colors[idx] = color;
                }
            }
        }

        positionBuffer.SetData(m_points);
        colorBuffer.SetData(colors);

        visualEffect.SetGraphicsBuffer("positionBuffer", positionBuffer);
        visualEffect.SetGraphicsBuffer("colorBuffer", colorBuffer);
        visualEffect.SetInt("ParticleCount", ParticleCount);
    }

    void LateUpdate()
    {
        var rebuild = new KnnRebuildJob(m_container);
        var rebuildHandle = rebuild.Schedule();

        if (!m_queryPositions.IsCreated || m_queryPositions.Length != QueryProbe.All.Count)
        {
            if (m_queryPositions.IsCreated)
            {
                m_queryPositions.Dispose();
                m_results.Dispose();
                m_queryColors.Dispose();
            }

            m_queryPositions = new NativeArray<float3>(QueryProbe.All.Count, Allocator.Persistent);
            m_results = new NativeArray<int>(QueryK * QueryProbe.All.Count, Allocator.Persistent);
            m_queryColors = new NativeArray<Color32>(QueryProbe.All.Count, Allocator.Persistent);

            if (m_rangeResults.IsCreated)
            {
                foreach (var result in m_rangeResults) result.Dispose();
                m_rangeResults.Dispose();
            }

            m_rangeResults = new NativeArray<RangeQueryResult>(QueryProbe.All.Count, Allocator.Persistent);
            for (int i = 0; i < m_rangeResults.Length; ++i)
            {
                m_rangeResults[i] = new RangeQueryResult(1024, Allocator.Persistent);
            }
        }

        for (int i = 0; i < QueryProbe.All.Count; i++)
        {
            var p = QueryProbe.All[i];
            m_queryPositions[i] = p.transform.position;
            m_queryColors[i] = p.Color;
        }

        switch (Mode)
        {
            case QueryMode.KNearest:
                {
                    var query = new QueryKNearestBatchJob(m_container, m_queryPositions, m_results);
                    query.ScheduleBatch(m_queryPositions.Length, 1, rebuildHandle).Complete();
                    break;
                }
            case QueryMode.Range:
                {
                    var query = new QueryRangeBatchJob(m_container, m_queryPositions, QueryRange, m_rangeResults);
                    query.ScheduleBatch(m_queryPositions.Length, 1, rebuildHandle).Complete();
                    break;
                }
        }
    }
}