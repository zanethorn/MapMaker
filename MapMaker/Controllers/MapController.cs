﻿using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Xml.Serialization;
using MapMaker.Models;
using MapMaker.Models.Map;
using MonitoredUndo;

namespace MapMaker.Controllers
{
    public class MapController : SmartObject
    {
        private const string KeyFileName = "map.xml";
        private const string ImageDirectory = "Resources/Images/";
        private EditorController _editorController;

        public MapController()
        {
            NewMap();
        }


        public Task Init()
        {
            return Task.CompletedTask;
        }

        private EditorController EditorController =>
            _editorController ??= (EditorController) App.Current.FindResource(nameof(EditorController));


        public MapFile NewMap()
        {
            var map = new MapFile();
            map.Layers.Add(new MapLayer {Name = "UntitledLayer_1"});
            return map;
        }

        public async Task<MapFile> LoadMap(string filename, CancellationToken cancellationToken = default)
        {
            using var zip = ZipFile.Open(filename, ZipArchiveMode.Read);

            // write the basic map.xml key file
            var keyEntry = zip.GetEntry(KeyFileName) ?? zip.CreateEntry(KeyFileName);
            await using Stream keyStream = keyEntry.Open();
            var serializer = new DataContractSerializer(typeof(MapFile));
            var mapFile = (MapFile) serializer.ReadObject(keyStream)!;
            keyStream.Close();

            foreach (var image in mapFile.ImageFiles)
            {
                var entryName = $"{ImageDirectory}{image.Id}.{image.FileExtension}";
                var imgEntry = zip.GetEntry(entryName)!;
                var outStream = imgEntry.Open();
                var memStream = new MemoryStream();
                await outStream.CopyToAsync(memStream, cancellationToken);
                memStream.Seek(0, SeekOrigin.Begin);
                outStream.Close();

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = memStream;
                bitmap.EndInit();
                if (bitmap.CanFreeze)
                    bitmap.Freeze();
                image.Bitmap = bitmap;
            }

            return mapFile;
        }

        public async Task SaveMap(string filename, MapFile mapFile, CancellationToken cancellationToken = default)
        {
            using var zip = ZipFile.Open(filename, ZipArchiveMode.Update);

            // write the basic map.xml key file
            var keyEntry = zip.GetEntry(KeyFileName) ?? zip.CreateEntry(KeyFileName);
            await using Stream keyStream = keyEntry.Open();
            var settings = new DataContractSerializerSettings()
            {
                PreserveObjectReferences = true
            };
            var serializer = new DataContractSerializer(typeof(MapFile), settings);
            serializer.WriteObject(keyStream, mapFile);
            await keyStream.FlushAsync(cancellationToken);

            // Store off all images
            foreach (var image in mapFile.ImageFiles)
            {
                var entryName = $"{ImageDirectory}{image.Id}.{image.FileExtension}";
                if (zip.GetEntry(entryName) == null)
                {
                    if (image.Bitmap.StreamSource == null)
                    {
                        zip.CreateEntryFromFile(image.Path, entryName);
                    }
                    else
                    {
                        var imgEntry = zip.CreateEntry(entryName);
                        await using var outStream = imgEntry.Open();
                        await image.Bitmap.StreamSource.CopyToAsync(outStream, cancellationToken);
                        await outStream.FlushAsync(cancellationToken);
                    }
                }
            }
        }

        public void AddLayer(MapFile mapFile, MapLayer mapLayer, int index = -1)
        {
            EditorController.BeginUndo($"Add {mapLayer}", true);
            if (index == -1)
                mapFile.Layers.Add(mapLayer);
            else
                mapFile.Layers.Insert(index, mapLayer);
        }

        public void DeleteLayer(MapFile mapFile, MapLayer mapLayer)
        {
            EditorController.BeginUndo($"Delete {mapLayer}", true);
            mapFile.Layers.Remove(mapLayer);
        }

        public void AddObjectToLayer(MapFile mapFile, MapLayer mapLayer, MapObject mapObject)
        {
            EditorController.BeginUndo($"Add {mapObject}", true);
            mapLayer.MapObjects.Add(mapObject);

            switch (mapObject)
            {
                case MapImage mapImage:
                {
                    var linkedBitmap = mapFile.ImageFiles.SingleOrDefault(f => f.Id == mapImage.Image.Id);
                    if (linkedBitmap == null)
                    {
                        linkedBitmap = mapImage.Image;
                        mapFile.ImageFiles.Add(linkedBitmap);
                    }

                    mapImage.Image = linkedBitmap;
                    break;
                }
                case MapShape mapShape:
                {
                    var fillBrush =
                        mapFile.Brushes.SingleOrDefault(i => i.GetHashCode() == mapShape.FillBrush.GetHashCode());
                    if (fillBrush == null)
                    {
                        fillBrush = mapShape.FillBrush;
                        mapFile.Brushes.Add(fillBrush);
                    }

                    mapShape.FillBrush = fillBrush;

                    var strokeBrush =
                        mapFile.Brushes.SingleOrDefault(i => i.GetHashCode() == mapShape.StrokeBrush.GetHashCode());
                    if (strokeBrush == null)
                    {
                        strokeBrush = mapShape.StrokeBrush;
                        mapFile.Brushes.Add(strokeBrush);
                    }

                    mapShape.StrokeBrush = strokeBrush;
                    break;
                }
                case MapText mapText:
                {
                    var fillBrush =
                        mapFile.Brushes.SingleOrDefault(i => i.GetHashCode() == mapText.FillBrush.GetHashCode());
                    if (fillBrush == null)
                    {
                        fillBrush = mapText.FillBrush;
                        mapFile.Brushes.Add(fillBrush);
                    }

                    mapText.FillBrush = fillBrush;

                    if (!mapFile.Fonts.Contains(mapText.FontFamily))
                    {
                        mapFile.Fonts.Add(mapText.FontFamily);
                    }

                    break;
                }
            }
        }

        public void DeleteObjectFromLayer(MapFile mapFile, MapLayer mapLayer, MapObject mapObject)
        {
            EditorController.BeginUndo($"Delete {mapObject}", true);
            mapLayer.MapObjects.Remove(mapObject);
        }

        public void MoveObjectUp(MapFile mapFile, MapLayer mapLayer, MapObject mapObject)
        {
            EditorController.BeginUndo("Reorder Object");
            var oldIndex = mapLayer.MapObjects.IndexOf(mapObject);
            mapLayer.MapObjects.Move(oldIndex, oldIndex + 1);
        }

        public void MoveObjectTop(MapFile mapFile, MapLayer mapLayer, MapObject mapObject)
        {
            EditorController.BeginUndo("Reorder Object");
            var oldIndex = mapLayer.MapObjects.IndexOf(mapObject);
            mapLayer.MapObjects.Move(oldIndex, mapLayer.MapObjects.Count - 1);
        }

        public void MoveObjectBottom(MapFile mapFile, MapLayer mapLayer, MapObject mapObject)
        {
            EditorController.BeginUndo("Reorder Object");
            var oldIndex = mapLayer.MapObjects.IndexOf(mapObject);
            mapLayer.MapObjects.Move(oldIndex, 0);
        }

        public void MoveObjectDown(MapFile mapFile, MapLayer mapLayer, MapObject mapObject)
        {
            EditorController.BeginUndo("Reorder Object");
            var oldIndex = mapLayer.MapObjects.IndexOf(mapObject);
            mapLayer.MapObjects.Move(oldIndex, oldIndex - 1);
        }

        public void MoveLayerUp(MapFile mapFile, MapLayer mapLayer)
        {
            EditorController.BeginUndo("Reorder Layer");
            var oldIndex = mapFile.Layers.IndexOf(mapLayer);
            mapFile.Layers.Move(oldIndex, oldIndex + 1);
        }

        public void MoveLayerDown(MapFile mapFile, MapLayer mapLayer)
        {
            EditorController.BeginUndo("Reorder Layer");
            var oldIndex = mapFile.Layers.IndexOf(mapLayer);
            mapFile.Layers.Move(oldIndex, oldIndex - 1);
        }

        public void MoveLayerTop(MapFile mapFile, MapLayer mapLayer)
        {
            EditorController.BeginUndo("Reorder Layer");
            var oldIndex = mapFile.Layers.IndexOf(mapLayer);
            mapFile.Layers.Move(oldIndex, mapFile.Layers.Count - 1);
        }

        public void MoveLayerBottom(MapFile mapFile, MapLayer mapLayer)
        {
            EditorController.BeginUndo("Reorder Layer");
            var oldIndex = mapFile.Layers.IndexOf(mapLayer);
            mapFile.Layers.Move(oldIndex, 0);
        }

        public void AddBrush(MapFile mapFile, MapBrush brush)
        {
            EditorController.BeginUndo("Create Brush", true);
            mapFile.Brushes.Add(brush);
        }

        public void MoveResizeObject(MapObject mapObject, Point location, Size newSize)
        {
            EditorController.BeginUndo($"Move/ Resize {mapObject}");
            mapObject.Offset = location;
            mapObject.Size = newSize;
        }
    }
}