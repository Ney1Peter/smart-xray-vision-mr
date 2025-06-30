using UnityEngine;
using Oculus.Interaction;
using Oculus.Interaction.DistanceReticles;

namespace Oculus.Interaction.DistanceReticles
{
    /// <summary>
    /// Draws a mesh outline of any GameObject that has a ReticleDataMesh component,
    /// and is currently hovered by a distance interactor. Does not apply any rotation.
    /// </summary>
    public class ReticleMeshDrawerDistanceInteractor : InteractorReticle<ReticleDataMesh>
    {
        [SerializeField, Interface(typeof(IDistanceInteractor))]
        private UnityEngine.Object _distanceInteractor;
        private IDistanceInteractor DistanceInteractor { get; set; }

        [SerializeField]
        private MeshFilter _filter;

        [SerializeField]
        private MeshRenderer _renderer;

        [SerializeField]
        private PoseTravelData _travelData = PoseTravelData.FAST;
        public PoseTravelData TravelData
        {
            get => _travelData;
            set => _travelData = value;
        }

        protected override IInteractorView Interactor { get; set; }
        protected override Component InteractableComponent => DistanceInteractor.DistanceInteractable as Component;

        private Tween _tween;

        protected virtual void Awake()
        {
            DistanceInteractor = _distanceInteractor as IDistanceInteractor;
            Interactor = DistanceInteractor;
        }

        protected override void Start()
        {
            this.BeginStart(ref _started, () => base.Start());
            this.AssertField(DistanceInteractor, nameof(_distanceInteractor));
            this.AssertField(_filter, nameof(_filter));
            this.AssertField(_renderer, nameof(_renderer));
            this.EndStart(ref _started);
        }

        protected override void Draw(ReticleDataMesh dataMesh)
        {
            _filter.sharedMesh = dataMesh.Filter.sharedMesh;
            _filter.transform.localScale = dataMesh.Filter.transform.lossyScale;
            _renderer.enabled = true;

            Pose target = dataMesh.Target.GetPose(); // No rotation adjustment
            _tween = _travelData.CreateTween(_filter.transform.GetPose(), target);
        }

        protected override void Align(ReticleDataMesh data)
        {
            Pose target = data.Target.GetPose();
            _tween.UpdateTarget(target);
            _tween.Tick();
            _filter.transform.SetPose(_tween.Pose);
        }

        protected override void Hide()
        {
            _tween = null;
            _renderer.enabled = false;
        }

        #region Inject
        public void InjectAllReticleMeshDrawerDistanceInteractor(IDistanceInteractor distanceInteractor,
            MeshFilter filter, MeshRenderer renderer)
        {
            InjectDistanceInteractor(distanceInteractor);
            InjectFilter(filter);
            InjectRenderer(renderer);
        }

        public void InjectDistanceInteractor(IDistanceInteractor distanceInteractor)
        {
            _distanceInteractor = distanceInteractor as UnityEngine.Object;
            DistanceInteractor = distanceInteractor;
            Interactor = distanceInteractor;
        }

        public void InjectFilter(MeshFilter filter)
        {
            _filter = filter;
        }

        public void InjectRenderer(MeshRenderer renderer)
        {
            _renderer = renderer;
        }
        #endregion
    }
}
