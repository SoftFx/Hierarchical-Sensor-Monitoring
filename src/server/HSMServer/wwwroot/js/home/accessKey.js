function showLargeModal() {
    var modal = document.getElementById("accessKeys_modalDialog");
    modal.classList.remove("w-50");
    modal.classList.add("w-75");
}

function showMiddleModal() {
    var modal = document.getElementById("accessKeys_modalDialog");
    modal.classList.remove("w-75");
    modal.classList.add("w-50");
}

function setModalTitle(title) {
    $('#accessKeys_modalTitle').empty();
    $('#accessKeys_modalTitle').append(title);
}

function setModalBody(viewData) {
    $("#accessKeys_modalBody").html(viewData);
}

function showAccessKeysListModal() {
    showLargeModal();

    let productName = $('#accessKey_productName').val();
    setModalTitle(`Access keys list for product '${productName}'`);
}

function showModal() {
    $('#accessKeys_modal').modal({
        backdrop: 'static',
    });
    $('#accessKeys_modal').modal('show');
}

function hideModal() {
    $('#accessKeys_modal').modal('hide');
}


function showAccessKeysList(productId, showModalFirst) {
    $.ajax({
        type: 'get',
        url: showProductAccessKeyTable + '?Selected=' + productId,
        datatype: 'html',
        contenttype: 'application/json',
        cache: false,
        success: function (viewData) {
            setModalBody(viewData);
        }
    }).done(function () {
        showAccessKeysListModal();

        if (showModalFirst === true) {
            showModal();
        }
    });
}

function showNewAccessKeyModal(url, openModal) {
    $.ajax({
        type: 'get',
        url: url,
        datatype: 'html',
        contenttype: 'application/json',
        cache: false,
        success: function (viewData) {
            setModalBody(viewData);
        }
    }).done(function () {
        if (openModal === true) {
            showModal();
        }

        showMiddleModal();
        setModalTitle("New access key");
    });
}