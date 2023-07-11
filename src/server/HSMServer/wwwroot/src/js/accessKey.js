window.setModalBody = function(viewData) {
    $("#accessKeys_modalBody").html(viewData);
}

window.showAccessKeysListModal = function() {
    showLargeModal();

    let productName = $('#accessKey_productName').val();
    setModalTitle(`Access keys for product '${productName}'`);
}

window.hideModal = function() {
    $('#accessKeys_modal').modal('hide');
    $('#editSensorStatus_form').trigger(jQuery.Event('hideNewAccessKeyModal'));
}

window.showAccessKeysList = function(productId, showModalFirst) {
    $.ajax({
        type: 'get',
        url: showProductAccessKeyTable + '?productId=' + productId,
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

window.showNewAccessKeyModal = function(url, openModal) {
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

window.changeAccessKey = function(url, id, name) {
    const isModalOpen = $('#accessKeys_modal').is(':visible')
    $.ajax({
        type: 'GET',
        url: `${url}?SelectedKey=${id}&CloseModal=${!isModalOpen}`,
        cache: false,
        async: true,
        success: function (viewData) {
            setModalBody(viewData);
        }
    }).done(function () {
        showModal();
        showMiddleModal();
        setModalTitle(`Edit access key '${name}'`);
    });
}

window.deleteAccessKey = function(url, id, name) {
    showConfirmationModal(
        `Removing ${name} access key`,
        `Do you really want to remove selected access key <strong>${name}</strong> ('${name}')?`,
        function () {
            $.ajax({
                type: 'POST',
                url: url,
                cache: false,
                async: true,
                success: function (viewData) {
                    $('#accessKeysTable').html(viewData);
                }
            })
        }
    );
}

window.blockAccessKey = function(url, id) {
    $.ajax({
        type: 'POST',
        url: url,
        cache: false,
        async: true,
        success: function (viewData) {
            $('#accessKeysTable').html(viewData);
        }
    })
}

function showModal() {
    $('#accessKeys_modal').modal({
        backdrop: 'static'
    });
    $('#accessKeys_modal').modal('show');
}

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