using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using DG.Tweening;
using UnityEngine.Events;


public class MenuEventSystemHandler : MonoBehaviour
{

    [Header("References")]
    public List<Selectable> Selectables = new List<Selectable>();
    [SerializeField] protected Selectable _firstSelected;

    [Header("Animations")]
    [SerializeField] protected float _selectedAnimationScale = 1.1f;
    [SerializeField] protected float _scaleDuration = 0.25f;
    [SerializeField] protected List<GameObject> _animationExclusions = new List<GameObject>();

    [Header("Sounds")]
    [SerializeField] protected UnityEvent SoundEvent;

    protected Dictionary<Selectable, Vector3> _scales = new Dictionary<Selectable, Vector3>();

    protected Selectable _lastSelected;

    protected Tween _scaleUpTween;
    protected Tween _scaleDownTween;

    public virtual void Awake()
    {
        foreach (var selectable in Selectables)
        {
            AddSelectionListeners(selectable);
            _scales.Add(selectable, selectable.transform.localScale);
            
        }
    }
    public virtual void OnEnable()
    {
        if (_firstSelected == null)
        {
            Debug.LogWarning($"{name}: _firstSelected is not assigned.", this);
        }

        for (int i = 0; i < Selectables.Count; i++)
        {
            Selectables[i].transform.localScale = _scales[Selectables[i]];
        }
        StartCoroutine(SelectAfterDelay()); 
        InputManager.Instance.OnNavigate += OnNavigate;
    }

    protected virtual IEnumerator SelectAfterDelay()
    {
        yield return null;
        if (EventSystem.current != null && _firstSelected != null)
        {
            EventSystem.current.SetSelectedGameObject(_firstSelected.gameObject);
        }
    }

    public virtual void OnDisable()
    {
        InputManager.Instance.OnNavigate -= OnNavigate;
        _scaleUpTween?.Kill(false);
        _scaleDownTween?.Kill(false);
    }

    protected virtual void AddSelectionListeners(Selectable selectable)
    {
        EventTrigger trigger = selectable.gameObject.GetComponent<EventTrigger>();
            
        if (trigger == null)
        {
            trigger = selectable.gameObject.AddComponent<EventTrigger>();
        }

        EventTrigger.Entry SelectEntry = new EventTrigger.Entry
        {
            eventID = EventTriggerType.Select
        };

        SelectEntry.callback.AddListener(OnSelect);
        trigger.triggers.Add(SelectEntry);

        EventTrigger.Entry DeselectEntry = new EventTrigger.Entry
        {
            eventID = EventTriggerType.Deselect
        };

        DeselectEntry.callback.AddListener(OnDeselect);
        trigger.triggers.Add(DeselectEntry);

        EventTrigger.Entry PointerEnter = new EventTrigger.Entry
        {
            eventID = EventTriggerType.PointerEnter
        };
        PointerEnter.callback.AddListener(OnPointerEnter);
        trigger.triggers.Add(PointerEnter);

        EventTrigger.Entry PointerExit = new EventTrigger.Entry
        {
            eventID = EventTriggerType.PointerExit
        };
        PointerExit.callback.AddListener(OnPointerExit);
        trigger.triggers.Add(PointerExit);
    }

    public void OnSelect(BaseEventData eventData)
    {
        if (eventData?.selectedObject == null)
            return;

        SoundEvent?.Invoke();
        _lastSelected =  eventData.selectedObject.GetComponent<Selectable>();
        if(_animationExclusions.Contains(eventData.selectedObject))
            return;
        Vector3 newScale = eventData.selectedObject.transform.localScale * _selectedAnimationScale;
        _scaleUpTween?.Kill(false);
        _scaleUpTween = eventData.selectedObject.transform
            .DOScale(newScale, _scaleDuration)
            .SetUpdate(true);

    }

    public void OnDeselect(BaseEventData eventData)
    {
        if (eventData?.selectedObject == null)
            return;

        if(_animationExclusions.Contains(eventData.selectedObject))
            return;
        Selectable sel = eventData.selectedObject.GetComponent<Selectable>();
        if (sel == null || !_scales.ContainsKey(sel))
            return;

        _scaleDownTween?.Kill(false);
        _scaleDownTween = eventData.selectedObject.transform
            .DOScale(_scales[sel], _scaleDuration)
            .SetUpdate(true);

    }

    public void OnPointerEnter(BaseEventData eventData)
    {
        PointerEventData pointerEventData = eventData as PointerEventData;

        if (pointerEventData != null && pointerEventData.pointerEnter != null)
        {
            Selectable sel = pointerEventData.pointerEnter.GetComponentInParent<Selectable>();
            if (sel is null)
            {
                sel = pointerEventData.pointerEnter.GetComponentInChildren<Selectable>();
            }
            if (sel == null)
                return;

            pointerEventData.selectedObject = sel.gameObject;
        }
    }

    public void OnPointerExit(BaseEventData eventData)
    {
        PointerEventData pointerEventData = eventData as PointerEventData;

        if (pointerEventData != null)
        {
            pointerEventData.selectedObject = null;
        }
    }

    protected virtual void OnNavigate()
    {
        if (EventSystem.current.currentSelectedGameObject == null && _lastSelected != null)
        {
            EventSystem. current. SetSelectedGameObject(_lastSelected.gameObject);
        }
    }
}
