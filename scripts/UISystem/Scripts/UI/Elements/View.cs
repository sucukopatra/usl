using UnityEngine;
using UnityEngine.UI;

public class View : CustomUIComponent
{
    public ViewSO viewData;

    public GameObject containerTop;
    public GameObject containerCenter;
    public GameObject containerBottom;

    private Image imageTop;
    private Image imageCenter;
    private Image imageBottom;

    private VerticalLayoutGroup verticalLayoutGroup;

    public override void Setup() {
        verticalLayoutGroup = GetComponent<VerticalLayoutGroup>();

        if (containerTop != null)
            imageTop = containerTop.GetComponent<Image>();

        if (containerCenter != null)
            imageCenter = containerCenter.GetComponent<Image>();

        if (containerBottom != null)
            imageBottom = containerBottom.GetComponent<Image>();
    }

    public override void Configure() {
        if (verticalLayoutGroup != null) {
            verticalLayoutGroup.padding = viewData.padding;
            verticalLayoutGroup.spacing = viewData.spacing;
        }

        if (viewData.theme == null)
            return;

        if (imageTop != null)
            imageTop.color = viewData.theme.primary_bg;

        if (imageCenter != null)
            imageCenter.color = viewData.theme.secondary_bg;

        if (imageBottom != null)
            imageBottom.color = viewData.theme.tertiary_bg;
    }
}
