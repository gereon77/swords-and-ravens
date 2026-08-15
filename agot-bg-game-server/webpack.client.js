const webpack = require("webpack");
const HtmlWebpackPlugin = require("html-webpack-plugin");
const TerserPlugin = require("terser-webpack-plugin");
const WebpackObfuscator = require("webpack-obfuscator");

const ASSET_PATH = process.env.ASSET_PATH || '/static/';

module.exports = (env, argv) => {
    return {
        output: {
            path: __dirname + "/dist/",
            filename: "bundle.[contenthash].js",
            publicPath: ASSET_PATH
        },
        entry: "./src/client/client.tsx",
        target: "web",
        // No source maps in production so original TS/TSX source isn't exposed via browser devtools.
        devtool: false,
        module: {
            rules: [
                {
                    test: /\.tsx?$/,
                    use: "ts-loader",
                    exclude: /node_modules/
                },
                {
                    test:/\.(s*)css$/,
                    use:['style-loader','css-loader', 'sass-loader']
                },
                {
                    test: /\.(gif|png|jpe?g|svg)$/i,
                    use: [
                        'file-loader',
                        {
                            loader: 'image-webpack-loader'
                        }
                    ]
                },
                {
                    test: /\.(ogg|mp3|wav|mpe?g)$/i,
                    use: 'file-loader'
                },
                {
                    test: /\.(ico)$/i,
                    use: 'file-loader'
                },
            ]
        },
        resolve: {
            extensions: [".tsx", ".ts", ".js"],
            alias: {
                process: "process/browser"
            },
            fallback: {
                "crypto": false,
                "stream": require.resolve("stream-browserify")
            }
        },
        plugins: [
            new HtmlWebpackPlugin({
                template: "public/index.html"
            }),
            new webpack.EnvironmentPlugin({
                NODE_ENV: argv.mode,
                BUILD_HASH: "devel"
            }),
            new webpack.ProvidePlugin({
                process: 'process/browser',
            }),
            // Obfuscates variable/class/function/parameter names so the bundle isn't trivially readable.
            new WebpackObfuscator({
                rotateStringArray: true,
                stringArray: true,
                stringArrayEncoding: ["base64"],
                identifierNamesGenerator: "hexadecimal",
                renameGlobals: false
            }, [])
        ],
        optimization: {
            minimizer: [
                new TerserPlugin({
                    terserOptions: {
                        output: {
                            comments: false
                        }
                    }
                })
            ]
        }
    };
};
