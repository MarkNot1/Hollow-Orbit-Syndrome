using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class ComponentArea : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    void Awake()
    {
     
    }
    public void OnPointerEnter(PointerEventData eventData)
    {
        Circuit_ConnectionManager.Instance.IsMouseOverComponent = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        Circuit_ConnectionManager.Instance.IsMouseOverComponent = false;
    }
}
