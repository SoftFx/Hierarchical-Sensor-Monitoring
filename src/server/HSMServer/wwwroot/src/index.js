import $ from 'jquery';

import * as bootstrap from 'bootstrap';
window.bootstrap = bootstrap;
bootstrap.Toast.Default.delay = 3000;

import 'bootstrap/dist/css/bootstrap.min.css';

import 'bootstrap-select';
import 'bootstrap-select/dist/css/bootstrap-select.css';

window.ClipboardJS = require('clipboard');

import * as moment from 'moment';
window.moment = moment;

import 'jstree/dist/jstree.min.js';
import 'jstree/dist/themes/default/style.min.css';

import * as Plotly from 'plotly.js/dist/plotly';
window.Plotly = Plotly;

import * as TimeSpan from 'timespan';
window.TimeSpan = TimeSpan;

import * as Marked from 'marked'
window.marked = Marked;

import * as DOMPurify from 'dompurify';
window.DOMPurify = DOMPurify;

import interact from "interactjs";

interact('.dropzone').dropzone({
    // only accept elements matching this CSS selector
    // Require a 75% element overlap for a drop to be possible
    overlap: 0.75,

    // listen for drop related events:

    ondropactivate: function (event) {
        // add active dropzone feedback
        event.target.classList.add('drop-active')
    },
    ondragenter: function (event) {
        var draggableElement = event.relatedTarget
        var dropzoneElement = event.target

        // feedback the possibility of a drop
        dropzoneElement.classList.add('drop-target')
        draggableElement.classList.add('can-drop')
        draggableElement.textContent = 'Dragged in'
    },
    ondragleave: function (event) {
        // remove the drop feedback style
        event.target.classList.remove('drop-target')
        event.relatedTarget.classList.remove('can-drop')
        event.relatedTarget.textContent = 'Dragged out'
    },
    ondrop: function (event) {
        event.relatedTarget.textContent = 'Dropped'
    },
    ondropdeactivate: function (event) {
        // remove active dropzone feedback
        event.target.classList.remove('drop-active')
        event.target.classList.remove('drop-target')
    }
})

interact('.drag-drop')
    .draggable({
        inertia: true,
        modifiers: [
            interact.modifiers.restrictRect({
                restriction: 'parent',
                endOnly: true
            })
        ],
        autoScroll: true,
        // dragMoveListener from the dragging demo above
        listeners: { move: dragMoveListener }
    })

import 'datatables';
import 'datatables/media/css/jquery.dataTables.min.css';

import '@fortawesome/fontawesome-free/js/all.min.js';
import '@fortawesome/fontawesome-free/css/all.min.css';

import 'emojionearea/dist/emojionearea.min.css'
import 'emojionearea/dist/emojionearea.min.js'

import './js/plots.js'
import './js/accessKey.js';
import './js/confirmation.js';
import './js/file.js';
import './js/history.js';
import './js/plotting.js';
import './js/metaInfo.js';
import './js/products.js';
import './js/tree.js';
import './js/site.js';
import './js/treeCollapse';

import './css/site.css';
import './css/accessKey.css';
import './css/home.css';
import './css/product.css';