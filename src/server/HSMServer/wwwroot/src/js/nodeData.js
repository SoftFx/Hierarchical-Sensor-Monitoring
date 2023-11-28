var needToActivateListTab = false;

window.currentSelectedNodeId = "";


window.initializeTreeNode = function () {
    $('#jstree').on('activate_node.jstree', function (e, data) {
        if (data.node.id != undefined) {
            selectNodeAjax(data.node.id);
        }
    }).on("state_ready.jstree", function () {
        let selected = $(this).jstree('get_selected')[0];
        
        if (window.sessionStorage.redirectIds){
            let ids = window.sessionStorage.redirectIds.split(',');

            selected = ids.at(-1);
            $(this).jstree('load_node', ids, function (){
                $(this).jstree('select_node', selected)
            })
            window.sessionStorage.removeItem('redirectIds');
        }

        selectNodeAjax(selected);
    });
}

window.activateNode = function (currentNodeId, nodeIdToActivate) {
    needToActivateListTab = $(`#list_${currentNodeId}`).hasClass('active');

    $('#jstree').jstree('activate_node', nodeIdToActivate);
    $('#jstree').jstree('open_node', nodeIdToActivate);

    if (currentSelectedNodeId != nodeIdToActivate) {
        selectNodeAjax(nodeIdToActivate);
    }
}


function selectNodeAjax(selectedId) {
    if (currentSelectedNodeId == selectedId || selectedId == undefined)
        return;

    let isEditMode = !$("#description").hasClass("d-none") && currentSelectedNodeId !== "";

    if (isEditMode) {
        saveMetaData(selectedId);
    }
    else {
        initSelectedNode(selectedId);
    }
}

function saveMetaData(selectedId) {
    let form = document.getElementById("editMetaInfo_form");
    let formData = new FormData(form);
    collectAlerts(formData);

    $.ajax({
        type: 'POST',
        url: isDataValidAction,
        data: formData,
        processData: false,
        contentType: false,
        async: true
    }).done(function (isValid) {
        let isAlertsValid = true;
        $("#editMetaInfo_form").find("div.dataAlertRow").each(function () {
            $(this).find(`input[name='Comment']`).each(function () {
                isAlertsValid &= $(this)[0].checkValidity();
            });

            $(this).find('input[name="Target"]').each(function () {
                isAlertsValid &= $(this)[0].checkValidity();
            });
        });

        if (isValid && isAlertsValid) {
            let path = $("#nodeHeader").text();

            showConfirmationModal(
                `Saving changes`,
                `Do you want to save '${path}' changes?`,
                () => {
                    $.ajax({
                        url: form.action,
                        type: 'POST',
                        data: formData,
                        processData: false,
                        contentType: false,
                        async: true
                    }).done(() => initSelectedNode(selectedId));
                },
                () => initSelectedNode(selectedId),
                "Yes",
                "No"
            );
        }
        else {
            initSelectedNode(selectedId);
        }
    });
}

function initSelectedNode(selectedId) {
    currentSelectedNodeId = selectedId;

    // Show spinner only if selected tree node contains 20 children (nodes/sensors) or it is sensor (doesn't have children)
    var selectedNode = $('#jstree').jstree().get_node(selectedId);
    if (!selectedNode || selectedNode.children.length > 20 || selectedNode.children.length == 0) {
        $("#nodeDataSpinner").css("display", "block");
        $('#nodeDataPanel').addClass('hidden_element');
    }

    $.ajax({
        type: 'post',
        url: `${selectNode}?selectedId=${selectedId}`,
        datatype: 'html',
        contenttype: 'application/json',
        cache: false,
        success: function (viewData) {
            $("#nodeDataPanel").removeClass('d-none').html(viewData);
        }
    }).done(function () {
        initialize();

        if (needToActivateListTab) {
            selectNodeInfoTab("list", selectedId);
            needToActivateListTab = false;
        }
        else {
            selectNodeInfoTab("grid", selectedId);
        }

        $("#nodeDataSpinner").css("display", "none");
        $('#nodeDataPanel').removeClass('hidden_element');
    });
}

function selectNodeInfoTab(tab, selectedId) {
    let tabLink = document.getElementById(`${tab}Link_${selectedId}`);

    if (tabLink != null)
        tabLink.click();
}