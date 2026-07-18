using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OnePieceCardScanner.Recognition.TemplateMatching;

public sealed class CharacterTemplateLibrary
{
    private readonly Dictionary<char, List<CharacterTemplate>>
        _templates =
            new();

    public IReadOnlyDictionary<char, List<CharacterTemplate>>
        Templates =>
            _templates;

    public void Load(
        string templateRootFolder)
    {
        _templates.Clear();

        if (!Directory.Exists(
                templateRootFolder))
        {
            throw new DirectoryNotFoundException(
                templateRootFolder);
        }

        foreach (string folder in
                 Directory.GetDirectories(
                     templateRootFolder))
        {
            string folderName =
                Path.GetFileName(
                    folder);

            if (string.IsNullOrWhiteSpace(
                    folderName))
            {
                continue;
            }

            if (!TryFolderToCharacter(
                folderName,
                out char character))
            {
                continue;
            }

            List<CharacterTemplate> list =
                [];

            foreach (string file in
                     Directory.GetFiles(
                         folder,
                         "*.png"))
            {
                Mat image =
                    Cv2.ImRead(
                        file,
                        ImreadModes.Grayscale);

                if (image.Empty())
                {
                    continue;
                }

                list.Add(
                    new CharacterTemplate
                    {
                        Character =
                            character,

                        FilePath =
                            file,

                        Image =
                            image
                    });
            }

            if (list.Count > 0)
            {
                _templates.Add(
                    character,
                    list);
            }
        }
    }

    public IEnumerable<CharacterTemplate>
        GetAllTemplates()
    {
        return _templates.Values
            .SelectMany(
                list => list);
    }

    public IReadOnlyList<CharacterTemplate>
        GetTemplates(
            char character)
    {
        if (_templates.TryGetValue(
                character,
                out List<CharacterTemplate>? list))
        {
            return list;
        }

        return [];
    }

    private static bool TryFolderToCharacter(
    string folderName,
    out char character)
    {
        switch (folderName)
        {
            case "Dash":
                character = '-';
                return true;

            case "Slash":
                character = '/';
                return true;
        }

        if (folderName.Length == 1)
        {
            character = folderName[0];
            return true;
        }

        character = default;
        return false;
    }
}