import interact from "interactjs";
window.interact = interact;
function dragMoveListener (event) {
    // console.log('Listener:')
    // console.log(event)

    var target = event.target
    // keep the dragged position in the data-x/data-y attributes
    var x = (parseFloat(target.getAttribute('data-x')) || 0) + event.dx
    var y = (parseFloat(target.getAttribute('data-y')) || 0) + event.dy

    // translate the element
    target.style.transform = 'translate(' + x + 'px, ' + y + 'px)'

    // update the posiion attributes
    target.setAttribute('data-x', x)
    target.setAttribute('data-y', y)
}

// this function is used later in the resizing and gesture demos
window.dragMoveListener = dragMoveListener

// interact('.draggable')
//     .draggable({
//         // enable inertial throwing
//         inertia: true,
//         // keep the element within the area of it's parent
//         modifiers: [
//             interact.modifiers.restrictRect({
//                 restriction: 'parent',
//                 endOnly: true
//             })
//         ],
//         // enable autoScroll
//         autoScroll: true,
//
//         listeners: {
//             // call this function on every dragmove event
//             move: dragMoveListener,
//
//             // call this function on every dragend event
//             end (event) {
//                 console.log('End:')
//                 console.log(event)
//                 var textEl = event.target.querySelector('p')
//
//                 textEl && (textEl.textContent =
//                     'moved a distance of ' +
//                     (Math.sqrt(Math.pow(event.pageX - event.x0, 2) +
//                         Math.pow(event.pageY - event.y0, 2) | 0))
//                         .toFixed(2) + 'px')
//             }
//         }
//     })
//
// interact('.dropzone').dropzone({
//     // only accept elements matching this CSS selector
//     // Require a 75% element overlap for a drop to be possible
//     overlap: 0.75,
//
//     // listen for drop related events:
//
//     ondropactivate: function (event) {
//         // add active dropzone feedback
//         event.target.classList.add('drop-active')
//     },
//     ondragenter: function (event) {
//         // console.log('On drag enter event:')
//         // console.log(event)
//
//         var draggableElement = event.relatedTarget
//         var dropzoneElement = event.target
//
//         // feedback the possibility of a drop
//         dropzoneElement.classList.add('drop-target')
//         draggableElement.classList.add('can-drop')
//     },
//     ondragleave: function (event) {
//         // console.log('On drag leave event:')
//         // console.log(event)
//         // remove the drop feedback style
//         event.target.classList.remove('drop-target')
//         event.relatedTarget.classList.remove('can-drop')
//     },
//     ondrop: function (event) {
//         alert(event.relatedTarget.id
//             + ' was dropped into '
//             + event.target.id)
//         console.log('On drop event:')
//         console.log(event)
//     },
//     ondropdeactivate: function (event) {
//         // remove active dropzone feedback
//         event.target.classList.remove('drop-active')
//         event.target.classList.remove('drop-target')
//         console.log(event)
//     }
// })

interact('.drag-drop')
    .draggable({
        inertia: true,
        modifiers: [
            // interact.modifiers.restrictRect({
            //     restriction: 'parent',
            //     endOnly: true
            // })
        ],
        autoScroll: true,
        // dragMoveListener from the dragging demo above
        listeners: {
            start (event) {
                event.target.style.position = "fixed";
            },
            move: dragMoveListener,
            end: showEventInfo
        }
    })

function showEventInfo (event) {
    console.log('On end:')
    console.log(event)
    event.target.style.transform = '';
    event.target.style.position = 'relative';
    event.target.removeAttribute('data-x')
    event.target.removeAttribute('data-y')
} 