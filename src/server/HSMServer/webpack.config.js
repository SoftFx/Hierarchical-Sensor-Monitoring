const path = require("path");
const webpack = require('webpack');

const CopyPlugin = require("copy-webpack-plugin");
const MomentLocalesPlugin = require('moment-locales-webpack-plugin');

module.exports = {
    target: 'web',
    entry: "./wwwroot/src/index.js",
    output: {
        path: path.resolve(__dirname, "wwwroot/dist"),
        filename: "[name].bundle.js",
        publicPath: "/dist/"
    },
    module: {
        rules: [
            {
                test: require.resolve('jquery'),
                loader: 'expose-loader',
                options: {
                    exposes: ['$', 'jQuery'],
                },
            },
            {
                test: /\.css$/,
                use: ['style-loader', 'css-loader']
            },
            {
                test: /\.ts?$/,
                use: 'ts-loader',
                exclude: /node_modules/,
            },
        ]
    },
    resolve: {
        extensions: ['.ts', '.js'],
        fallback: {
            stream: require.resolve("stream-browserify"),
            os: require.resolve("os-browserify"),
            process: require.resolve("process/browser"),
            assert: require.resolve("assert/")
        }
    },
    plugins: [
        new webpack.ProvidePlugin({
            process: 'process',
        }),
        new webpack.ProvidePlugin({
            $: 'jquery',
            jQuery: 'jquery',
        }),
        new webpack.ProvidePlugin({
            'window.Dropdown': ['bootstrap', 'Dropdown'],
        }),
        new CopyPlugin({
            patterns: [
                {
                    from: path.resolve(__dirname, "wwwroot/src/svg"),
                    to: ""
                }
            ]
        }),
        new MomentLocalesPlugin()
    ],
};