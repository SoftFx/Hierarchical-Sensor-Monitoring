let addKeyButtonId = "addAccessKeyButton";
let saveKeyButtonId = "saveAccessKeyButton";
let cancelButtonId = "cancelAccessKeyButton";


function buttonsForAccessKeysListModal() {
    showButton(addKeyButtonId);
    hideButton(saveKeyButtonId);
    hideButton(cancelButtonId);
}

function buttonsForNewAccessKeyModal() {
    hideButton(addKeyButtonId);
    showButton(saveKeyButtonId);
    showButton(cancelButtonId);
}

function setModalTitle(title) {
    $('#accessKeys_modalTitle').empty();
    $('#accessKeys_modalTitle').append(title);
}

function setModalBody(viewData) {
    $("#accessKeys_modalBody").html(viewData);
}

function showButton(buttonId) {
    let element = document.getElementById(buttonId);
    element.removeAttribute("hidden");
}

function hideButton(buttonId) {
    let element = document.getElementById(buttonId);
    element.setAttribute("hidden", "hidden");
}

function setTitleAndButtonsForAccessKeysListModal() {
    setModalTitle("Access keys list for product");
    buttonsForAccessKeysListModal();
}


function showAccessKeysList(productId, showModalFirst) {
    $.ajax({
        type: 'get',
        url: getAccessKeysList + '?Selected=' + productId,
        datatype: 'html',
        contenttype: 'application/json',
        cache: false,
        success: function (viewData) {
            setModalBody(viewData);
        }
    }).done(function () {
        setTitleAndButtonsForAccessKeysListModal();

        if (showModalFirst === true) {
            console.log(showModalFirst);
            document.getElementById("accessKey_prodcutId").setAttribute('value', productId);
            $('#accessKeys_modal').modal('show')
        }
    });
}