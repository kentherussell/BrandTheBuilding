const fs = require("fs");
const path = require("path");
const MiniCssExtractPlugin = require("mini-css-extract-plugin");
const { CSSPresencePlugin } = require("./tools/css-presence");
const MOD = require("./mod.json");

function resolveUserDataPath() {
  if (process.env.CSII_USERDATAPATH?.trim()) {
    return process.env.CSII_USERDATAPATH.trim();
  }

  if (process.platform === "win32" && process.env.USERPROFILE?.trim()) {
    return path.join(
      process.env.USERPROFILE.trim(),
      "AppData",
      "LocalLow",
      "Colossal Order",
      "Cities Skylines II"
    );
  }

  throw new Error("Set CSII_USERDATAPATH before building the UI.");
}

const outputDirectory = path.join(resolveUserDataPath(), "Mods", MOD.id);
fs.mkdirSync(outputDirectory, { recursive: true });

module.exports = {
  mode: "production",
  stats: { preset: "errors-warnings", errorDetails: true },
  entry: { [MOD.id]: "./src/index.tsx" },
  externalsType: "window",
  externals: {
    react: "React",
    "react-dom": "ReactDOM",
    "cs2/modding": "cs2/modding",
    "cs2/api": "cs2/api",
    "cs2/ui": "cs2/ui"
  },
  module: {
    rules: [
      {
        test: /\.tsx?$/,
        use: { loader: "ts-loader", options: { transpileOnly: true } },
        exclude: /node_modules/
      },
      {
        test: /\.s?css$/,
        include: path.join(__dirname, "src"),
        use: [
          MiniCssExtractPlugin.loader,
          {
            loader: "css-loader",
            options: {
              importLoaders: 1,
              modules: {
                auto: true,
                exportLocalsConvention: "camelCase",
                localIdentName: "[local]_[hash:base64:5]"
              }
            }
          },
          {
            loader: "sass-loader",
            options: { api: "modern" }
          }
        ]
      }
    ]
  },
  resolve: {
    extensions: [".tsx", ".ts", ".js"],
    modules: ["node_modules", path.join(__dirname, "src")],
    alias: { "mod.json": path.resolve(__dirname, "mod.json") }
  },
  plugins: [
    new MiniCssExtractPlugin({ filename: `${MOD.id}.css` }),
    new CSSPresencePlugin()
  ],
  output: {
    path: outputDirectory,
    filename: `${MOD.id}.mjs`,
    library: { type: "module" },
    publicPath: "coui://ui-mods/",
    clean: false
  },
  experiments: { outputModule: true }
};
