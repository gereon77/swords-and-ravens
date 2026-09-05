const webpack = require("webpack");
const HtmlWebpackPlugin = require("html-webpack-plugin");
const TerserPlugin = require("terser-webpack-plugin");

const ASSET_PATH = process.env.ASSET_PATH || '/static/';

module.exports = (env, argv) => {
    return {
        output: {
            path: __dirname + "/dist/",
            filename: "bundle.[contenthash].js",
            publicPath: ASSET_PATH,
            pathinfo: false,
            hashFunction: "xxhash64",
        },
        entry: "./src/client/client.tsx",
        target: "web",
        devtool: "inline-source-map",
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
                        // Explicit sha256 hash so loader-utils doesn't fall back to its default
                        // md4 digest, which OpenSSL 3 (Node.js 17+) refuses to compute.
                        { loader: 'file-loader', options: { name: '[sha256:contenthash:hex:16].[ext]' } },
                        {
                            loader: 'image-webpack-loader'
                        }
                    ]
                },
                {
                    test: /\.(ogg|mp3|wav|mpe?g)$/i,
                    use: { loader: 'file-loader', options: { name: '[sha256:contenthash:hex:16].[ext]' } }
                },
                {
                    test: /\.(ico)$/i,
                    use: { loader: 'file-loader', options: { name: '[sha256:contenthash:hex:16].[ext]' } }
                },
            ],
            unsafeCache: true,
        },
        resolve: {
            extensions: [".tsx", ".ts", ".js"],
            alias: {
                process: "process/browser"
            },
            fallback: {
                "crypto": false,
                "stream": require.resolve("stream-browserify")
            },
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
        ],
        optimization: {
            minimizer: [
                new TerserPlugin({
                    terserOptions: {
                        output: {
                            //comments: false
                        }
                    }
                })
            ],
            sideEffects: false,
            providedExports: false,
            usedExports: false,
            innerGraph: false,
            mergeDuplicateChunks: false,
            removeEmptyChunks: false,
        },
        cache: {
            type: 'filesystem',
            compression: 'gzip',
        }
    };
};
