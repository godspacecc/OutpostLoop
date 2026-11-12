using QFramework;
using TMPro;
using UnityEngine;

namespace Game
{
    public sealed class RedPointView : MonoBehaviour
    {
        [SerializeField] private string nodeId;
        [SerializeField] private GameObject markRoot;
        [SerializeField] private TMP_Text countText;
        private IUnRegister unregister;

        public string NodeId
        {
            get => nodeId;
            set => nodeId = value;
        }

        private void OnEnable()
        {
            Apply(Game.Interface.GetSystem<RedPointSystem>().GetState(nodeId));
            unregister = Game.Interface.RegisterEvent<RedPointChangedEvent>(OnRedPointChanged);
        }

        private void OnDisable()
        {
            unregister?.UnRegister();
            unregister = null;
        }

        private void OnRedPointChanged(RedPointChangedEvent e)
        {
            if (e.Id == nodeId)
            {
                Apply(new RedPointState(e.Id, e.Active, e.Count));
            }
        }

        private void Apply(RedPointState state)
        {
            if (markRoot != null)
            {
                markRoot.SetActive(state.Active);
            }

            if (countText != null)
            {
                countText.gameObject.SetActive(state.Active && state.Count > 1);
                countText.text = state.Count > 1 ? state.Count.ToString() : string.Empty;
            }
        }
    }
}
